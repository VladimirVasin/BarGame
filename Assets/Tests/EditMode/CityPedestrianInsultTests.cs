using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The street's insults: the twenty lines against the register the
    /// story bible's §6 registry row of 2026-09-05 holds them to, the
    /// seeded walk that hands them out, and the pure rules that decide who
    /// may say one and when. This is the canon test that keeps the lift
    /// exactly as narrow as the row says: a line that raises its voice,
    /// asks, swears, names a number, reaches for an abstraction or touches
    /// the crime fails the build rather than a review.
    /// </summary>
    public sealed class CityPedestrianInsultTests
    {
        private const int Seed = 20260905;

        /// <summary>§16.4 and §24.27: these words never sound in this game.</summary>
        private static readonly string[] ForbiddenWords =
        {
            "вина", "виноват", "алкоголизм", "зависимость",
            "галлюцинация", "социопат", "эпидемия",
            "guilt", "alcoholism", "addiction", "hallucination",
            "sociopath", "epidemic"
        };

        /// <summary>§21: no abstractions.</summary>
        private static readonly string[] ForbiddenAbstractions =
        {
            "судьба", "душа", "зло", "истина", "прощение",
            "fate", "soul", "evil", "truth", "forgiveness"
        };

        /// <summary>§16.1: a passer-by knows nothing and hints at nothing —
        /// not the crime, not the room, not the water, not the mother, not
        /// the cat. The insult is about the drunk in front of him today.</summary>
        private static readonly string[] ForbiddenSubjects =
        {
            "мать", "мама", "комнат", "вода", "воды", "воду", "могил",
            "ключ", "кот ", "кота", "коту",
            "mother", "room", "water", "grave", "the cat", "keys"
        };

        /// <summary>The user's decision: biting, never coarse.</summary>
        private static readonly string[] Profanity =
        {
            "бля", "хуй", "хуе", "пизд", "ёб", "еба", "ебл", "сука",
            "муда", "гандон", "залуп", "падл", "дерьм", "жоп",
            "fuck", "shit", "cunt", "bitch", "bastard", "asshole",
            "dick", "piss", "crap"
        };

        [Test]
        public void Lines_ResolveInBothCatalogsAndHoldTheStreetRegister()
        {
            Assert.That(
                CityPedestrianInsultLines.LineKeys.Length,
                Is.EqualTo(CityPedestrianInsultLines.LineCount));

            string[] resourcePaths = { "Localization/ru", "Localization/en" };
            for (int catalogIndex = 0;
                 catalogIndex < resourcePaths.Length;
                 catalogIndex++)
            {
                string resourcePath = resourcePaths[catalogIndex];
                Dictionary<string, string> values = LoadCatalog(resourcePath);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                string[] keys = CityPedestrianInsultLines.LineKeys;
                for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                {
                    string key = keys[keyIndex];
                    Assert.That(
                        values.ContainsKey(key),
                        Is.True,
                        $"{resourcePath} is missing '{key}'.");
                    string line = values[key];
                    AssertRegister(resourcePath, key, line);
                    Assert.That(
                        seen.Add(line),
                        Is.True,
                        $"{resourcePath} '{key}' repeats another line word " +
                        "for word.");
                }
            }
        }

        [Test]
        public void Lines_TwentyDistinctKeysUnderOnePrefix()
        {
            string[] keys = CityPedestrianInsultLines.LineKeys;
            Assert.That(keys.Length, Is.EqualTo(20));
            Assert.That(keys.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(20));
            for (int index = 0; index < keys.Length; index++)
            {
                Assert.That(
                    keys[index],
                    Does.StartWith("city.pedestrian.insult."),
                    "The street's pool lives under one prefix.");
            }
        }

        [Test]
        public void Lines_NeverRepeatBackToBackAndEveryLineComesUp()
        {
            uint state = CityPedestrianInsultLines.CreateState(Seed);
            uint twin = CityPedestrianInsultLines.CreateState(Seed);
            var counts = new int[CityPedestrianInsultLines.LineKeys.Length];
            int previous = -1;
            int twinPrevious = -1;
            for (int draw = 0; draw < 4000; draw++)
            {
                int index = CityPedestrianInsultLines.NextIndex(ref state, previous);
                int twinIndex = CityPedestrianInsultLines.NextIndex(ref twin, twinPrevious);
                Assert.That(index, Is.InRange(0, counts.Length - 1));
                Assert.That(index, Is.Not.EqualTo(previous),
                    "The street never says the same thing twice running.");
                Assert.That(twinIndex, Is.EqualTo(index),
                    "The same seed must walk the same way.");
                counts[index]++;
                previous = index;
                twinPrevious = twinIndex;
            }

            Assert.That(counts.Min(), Is.GreaterThan(0), "Every line has to come up.");
            Assert.That(CityPedestrianInsultLines.CreateState(Seed), Is.Not.Zero);
            Assert.That(
                CityPedestrianInsultLines.CreateState(Seed),
                Is.Not.EqualTo(CemeteryWatchmanQuips.CreateState(Seed)),
                "The street must not walk the watchman's walk.");
        }

        [Test]
        public void Rules_SpeakOnlyOnTheLastStage()
        {
            Assert.That(CityPedestrianInsultRules.IsInsultStage(0), Is.False);
            Assert.That(CityPedestrianInsultRules.IsInsultStage(60), Is.False);
            Assert.That(CityPedestrianInsultRules.IsInsultStage(61), Is.False);
            Assert.That(CityPedestrianInsultRules.IsInsultStage(80), Is.False);
            Assert.That(CityPedestrianInsultRules.IsInsultStage(81), Is.True);
            Assert.That(CityPedestrianInsultRules.IsInsultStage(100), Is.True);
            // The rearm distance sits past the attention release radius, so
            // a hero drifting on the cone's edge is neither re-noticed nor
            // re-insulted; and the remark is thrown from inside the notice
            // cone, well outside the shove.
            Assert.That(
                CityPedestrianInsultRules.ReleaseDistance,
                Is.GreaterThan(PlayerAttentionRules.ReleaseRadius));
            Assert.That(
                CityPedestrianInsultRules.SpeakDistance,
                Is.LessThan(PlayerAttentionRules.NoticeRadius)
                    .And.GreaterThan(CityPedestrianPersonalSpaceRules.ShoveDistance));
        }

        [Test]
        public void Rules_TheMournerKeepsHerSilenceAndABackTurnedDoesNotSpeak()
        {
            Assert.That(
                CityPedestrianInsultRules.MaySpeak(CityPedestrianResources.MournerDesignId),
                Is.False,
                "The bible keeps the mourner mute by name; her street copy stays mute.");
            string[] speaking =
            {
                CityPedestrianResources.BabushkaDesignId,
                CityPedestrianResources.WeighAttendantDesignId,
                CityPedestrianResources.WatchmanDesignId,
                CityPedestrianResources.ChessPlayerDesignId,
                CityPedestrianResources.CheckersPlayerDesignId
            };
            for (int index = 0; index < speaking.Length; index++)
            {
                Assert.That(
                    CityPedestrianInsultRules.MaySpeak(speaking[index]),
                    Is.True,
                    $"{speaking[index]} roams and speaks.");
            }

            Assert.That(CityPedestrianInsultRules.MaySpeak(null), Is.False);
            Assert.That(CityPedestrianInsultRules.MaySpeak(string.Empty), Is.False);

            Vector3 walker = Vector3.zero;
            Vector3 hero = new Vector3(0f, 0f, 2f);
            Assert.That(
                CityPedestrianInsultRules.IsFacing(Vector3.forward, walker, hero),
                Is.True);
            Assert.That(
                CityPedestrianInsultRules.IsFacing(Vector3.back, walker, hero),
                Is.False,
                "A back turned to the hero says nothing.");
            Assert.That(
                CityPedestrianInsultRules.IsFacing(
                    Quaternion.Euler(0f, 70f, 0f) * Vector3.forward, walker, hero),
                Is.True,
                "A man walking past at an angle still counts.");
            Assert.That(
                CityPedestrianInsultRules.IsFacing(Vector3.right, walker, hero),
                Is.False,
                "Square sideways is not facing him.");
            Assert.That(
                CityPedestrianInsultRules.IsFacing(Vector3.forward, hero, hero),
                Is.False);
        }

        private static void AssertRegister(
            string resourcePath,
            string key,
            string line)
        {
            Assert.That(
                line,
                Is.Not.Null.And.Not.Empty,
                $"{resourcePath} leaves '{key}' blank.");
            Assert.That(
                line.Length,
                Is.LessThanOrEqualTo(CityPedestrianInsultLines.MaximumLineLength),
                $"{resourcePath} '{key}' is too long for a two-row bubble: " +
                $"{line.Length}.");
            Assert.That(
                line.EndsWith(".", StringComparison.Ordinal),
                Is.True,
                $"{resourcePath} '{key}' must end as a statement.");
            Assert.That(
                line.Contains("!"),
                Is.False,
                $"{resourcePath} '{key}' raises its voice.");
            Assert.That(
                line.Contains("?"),
                Is.False,
                $"{resourcePath} '{key}' asks him something.");
            Assert.That(
                line.Contains("("),
                Is.False,
                $"{resourcePath} '{key}' explains itself in brackets.");
            Assert.That(
                line.Count(character => character == '.'),
                Is.InRange(1, 2),
                $"{resourcePath} '{key}' runs past two short sentences.");
            Assert.That(
                line.Any(char.IsDigit),
                Is.False,
                $"{resourcePath} '{key}' names a number.");

            string lowered = line.ToLowerInvariant();
            AssertNone(resourcePath, key, lowered, ForbiddenWords,
                "says '{0}', which never sounds in this game");
            AssertNone(resourcePath, key, lowered, ForbiddenAbstractions,
                "reaches for '{0}'");
            AssertNone(resourcePath, key, lowered, ForbiddenSubjects,
                "touches '{0}', which a passer-by knows nothing about");
            AssertNone(resourcePath, key, lowered, Profanity,
                "is coarse ('{0}')");
        }

        private static void AssertNone(
            string resourcePath,
            string key,
            string lowered,
            string[] banned,
            string reason)
        {
            for (int index = 0; index < banned.Length; index++)
            {
                Assert.That(
                    lowered.Contains(banned[index]),
                    Is.False,
                    $"{resourcePath} '{key}' " +
                    string.Format(reason, banned[index]) + ".");
            }
        }

        private static Dictionary<string, string> LoadCatalog(string resourcePath)
        {
            TextAsset asset = Resources.Load<TextAsset>(resourcePath);
            Assert.That(
                asset,
                Is.Not.Null,
                $"Expected a TextAsset at Resources/{resourcePath}.json.");
            Catalog catalog = JsonUtility.FromJson<Catalog>(asset.text);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.entries, Is.Not.Null);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < catalog.entries.Length; index++)
            {
                CatalogEntry entry = catalog.entries[index];
                Assert.That(entry, Is.Not.Null, $"{resourcePath} contains a null entry.");
                Assert.That(
                    values.ContainsKey(entry.key),
                    Is.False,
                    $"{resourcePath} contains duplicate key '{entry.key}'.");
                values.Add(entry.key, entry.value);
            }

            return values;
        }

        [Serializable]
        private sealed class Catalog
        {
            public CatalogEntry[] entries = Array.Empty<CatalogEntry>();
        }

        [Serializable]
        private sealed class CatalogEntry
        {
            public string key = string.Empty;
            public string value = string.Empty;
        }
    }
}
