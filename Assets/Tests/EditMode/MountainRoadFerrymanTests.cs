using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// What he says once the road has ended. The island's five contracts,
    /// held against the second pool — and one more, which is the whole
    /// difference between the two: up here he must not name what is above
    /// the cable, because he has not been there either.
    /// </summary>
    public sealed class MountainRoadFerrymanTests
    {
        [Test]
        [Category("MountainRoad")]
        public void MountainQuips_AreDeterministicAndNeverRepeatTwiceRunning()
        {
            List<int> first = Draw(GameSessionState.DefaultCitySeed, 60);
            List<int> second = Draw(GameSessionState.DefaultCitySeed, 60);
            CollectionAssert.AreEqual(first, second);

            for (int index = 1; index < first.Count; index++)
            {
                Assert.That(
                    first[index],
                    Is.Not.EqualTo(first[index - 1]),
                    "He answered himself twice running.");
            }

            var reached = new HashSet<int>(first);
            Assert.That(
                reached.Count,
                Is.EqualTo(
                    LastRouteFerrymanQuips.MountainLineKeys.Length),
                "Some line is unreachable.");
        }

        [Test]
        [Category("MountainRoad")]
        public void MountainQuips_WalkTheirOwnStream()
        {
            // Two pools off one seed marching in step would serve the same
            // ordinal answer in both places on the same visit.
            List<int> island = new List<int>();
            uint islandState = LastRouteFerrymanQuips.CreateState(
                GameSessionState.DefaultCitySeed);
            int previous = -1;
            for (int index = 0; index < 24; index++)
            {
                previous = LastRouteFerrymanQuips.NextIndex(
                    ref islandState,
                    previous);
                island.Add(previous);
            }

            CollectionAssert.AreNotEqual(
                island,
                Draw(GameSessionState.DefaultCitySeed, 24));
        }

        [Test]
        [Category("MountainRoad")]
        public void MountainQuips_OfferNothingAndPromiseNothing()
        {
            string[] forbiddenOffers =
            {
                "садись", "поехали", "поедем", "подвезу", "довезу",
                "get in", "let's go", "i'll drive you", "hop in",
                "ride with me"
            };

            // And the mountain's own silence: the cable goes somewhere and
            // he does not say where, because the game does not know either.
            string[] forbiddenDestinations =
            {
                "наверх", "вершин", "канатк", "кабин", "перевал",
                "summit", "cable", "top of", "up there"
            };

            Dictionary<string, string> russian =
                LoadCatalog("Localization/ru");
            Dictionary<string, string> english =
                LoadCatalog("Localization/en");

            string[] keys = LastRouteFerrymanQuips.MountainLineKeys;
            for (int index = 0; index < keys.Length; index++)
            {
                string key = keys[index];
                AssertFree(key, russian[key], forbiddenOffers);
                AssertFree(key, english[key], forbiddenOffers);
                AssertFree(key, russian[key], forbiddenDestinations);
                AssertFree(key, english[key], forbiddenDestinations);
            }
        }

        [Test]
        [Category("MountainRoad")]
        public void MountainQuips_ResolveInBothCatalogsAndStayShort()
        {
            Dictionary<string, string> russian =
                LoadCatalog("Localization/ru");
            Dictionary<string, string> english =
                LoadCatalog("Localization/en");

            var required = new List<string>(
                LastRouteFerrymanQuips.MountainLineKeys)
            {
                LastRouteFerrymanTalkInteraction.TalkPromptKey
            };

            for (int index = 0; index < required.Count; index++)
            {
                string key = required[index];
                Assert.That(
                    russian.ContainsKey(key),
                    Is.True,
                    $"ru.json has no '{key}'.");
                Assert.That(
                    english.ContainsKey(key),
                    Is.True,
                    $"en.json has no '{key}'.");
                Assert.That(
                    russian[key],
                    Is.Not.Null.And.Not.Empty);
                Assert.That(
                    english[key],
                    Is.Not.Null.And.Not.Empty);
            }

            string[] keys = LastRouteFerrymanQuips.MountainLineKeys;
            for (int index = 0; index < keys.Length; index++)
            {
                string line = russian[keys[index]];
                Assert.That(
                    line.Length,
                    Is.LessThanOrEqualTo(48),
                    $"'{keys[index]}' runs long for a man who does not " +
                    "explain.");
                Assert.That(
                    line.Contains("!"),
                    Is.False,
                    $"'{keys[index]}' raises its voice.");
            }
        }

        private static List<int> Draw(int seed, int count)
        {
            uint state = LastRouteFerrymanQuips.CreateMountainState(seed);
            int previous = -1;
            var drawn = new List<int>(count);
            for (int index = 0; index < count; index++)
            {
                previous = LastRouteFerrymanQuips.NextIndex(
                    ref state,
                    previous,
                    LastRouteFerrymanQuips.MountainLineKeys);
                drawn.Add(previous);
            }

            return drawn;
        }

        private static void AssertFree(
            string key,
            string line,
            string[] forbidden)
        {
            for (int index = 0; index < forbidden.Length; index++)
            {
                Assert.That(
                    line.IndexOf(
                        forbidden[index],
                        StringComparison.OrdinalIgnoreCase),
                    Is.LessThan(0),
                    $"'{key}' says '{forbidden[index]}': \"{line}\".");
            }
        }

        private static Dictionary<string, string> LoadCatalog(string path)
        {
            var asset = Resources.Load<TextAsset>(path);
            Assert.That(asset, Is.Not.Null, $"Missing catalog '{path}'.");
            var catalog = JsonUtility.FromJson<Catalog>(asset.text);
            var map = new Dictionary<string, string>(
                StringComparer.Ordinal);
            for (int index = 0; index < catalog.entries.Length; index++)
            {
                map[catalog.entries[index].key] =
                    catalog.entries[index].value;
            }

            return map;
        }

        [Serializable]
        private sealed class Catalog
        {
            public Entry[] entries;
        }

        [Serializable]
        private sealed class Entry
        {
            public string key;
            public string value;
        }
    }
}
