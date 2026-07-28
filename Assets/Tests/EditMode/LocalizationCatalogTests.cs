using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class LocalizationCatalogTests
    {
        private static readonly string[] RequiredKeys =
        {
            "interaction.enter_bar",
            "interaction.exit_bar",
            "interaction.order_drinks",
            "interaction.play_beer_pong",
            "interaction.play_split_the_g",
            "drinking.intoxication",
            "drinking.wasted",
            "cocktail.title",
            "cocktail.stage",
            "cocktail.choose_base",
            "cocktail.choose_base_hint",
            "cocktail.mix_hint",
            "cocktail.controls.choose",
            "cocktail.controls.add",
            "cocktail.controls.serve",
            "cocktail.controls.back",
            "cocktail.score.current",
            "cocktail.score.total",
            "cocktail.feedback.good",
            "cocktail.feedback.bad",
            "cocktail.result.round",
            "cocktail.result.final",
            "cocktail.result.wasted_early",
            "cocktail.rank.slop",
            "cocktail.rank.okay",
            "cocktail.rank.amateur",
            "cocktail.rank.master",
            "cocktail.rank.perfect",
            "cocktail.ingredient.beer",
            "cocktail.ingredient.wine",
            "cocktail.ingredient.vodka",
            "cocktail.ingredient.cognac",
            "cocktail.ingredient.tonic",
            "cocktail.ingredient.soda",
            "cocktail.ingredient.cola",
            "cocktail.ingredient.orange",
            "cocktail.ingredient.lemon",
            "cocktail.ingredient.ginger_ale",
            "cocktail.ingredient.honey",
            "cocktail.ingredient.mint",
            "cocktail.ingredient.berries",
            "cocktail.ingredient.cherry",
            "cocktail.ingredient.ice",
            "beerpong.title",
            "beerpong.score",
            "beerpong.throws",
            "beerpong.cups",
            "beerpong.intoxication",
            "beerpong.aim",
            "beerpong.power",
            "beerpong.controls.aim",
            "beerpong.controls.throw",
            "beerpong.controls.cancel",
            "beerpong.feedback.clean",
            "beerpong.feedback.bank",
            "beerpong.feedback.rim",
            "beerpong.feedback.bounce",
            "beerpong.feedback.miss",
            "beerpong.result.cleared",
            "beerpong.result.out_of_throws",
            "beerpong.result.wasted",
            "beerpong.result.final",
            "beerpong.result.continue",
            "splitg.title",
            "splitg.attempt",
            "splitg.best",
            "splitg.intoxication",
            "splitg.target",
            "splitg.countdown",
            "splitg.release_first",
            "splitg.ready",
            "splitg.drinking",
            "splitg.settling",
            "splitg.controls",
            "splitg.result.score",
            "splitg.result.error",
            "splitg.retry",
            "splitg.continue",
            "splitg.final",
            "splitg.rank.perfect",
            "splitg.rank.excellent",
            "splitg.rank.good",
            "splitg.rank.close",
            "splitg.rank.miss",
            "splitg.direction.under",
            "splitg.direction.over",
            "splitg.direction.target",
            "drink.light_beer",
            "drink.red_wine",
            "drink.vodka",
            "drink.cognac_vs",
            "drink.water",
            "map.open_hint",
            "map.title",
            "map.instructions",
            "map.route_title",
            "map.route_empty",
            "map.bar_name",
            "map.player",
            "map.clear",
            "map.visited_count",
            "map.distance",
            "debug.minigames.title",
            "debug.minigames.hint",
            "debug.minigames.controls",
            "debug.minigames.empty",
            "debug.minigames.unavailable",
            "debug.minigame.cocktail",
            "debug.minigame.beer_pong",
            "debug.minigame.split_the_g"
        };

        [TestCase("Localization/ru")]
        [TestCase("Localization/en")]
        public void Catalog_IsPresentAndContainsRequiredKeys(string resourcePath)
        {
            Dictionary<string, string> valuesByKey = LoadValues(resourcePath);

            for (int index = 0; index < RequiredKeys.Length; index++)
            {
                string key = RequiredKeys[index];
                Assert.That(
                    valuesByKey.ContainsKey(key),
                    Is.True,
                    $"{resourcePath} is missing '{key}'.");
                Assert.That(
                    valuesByKey[key],
                    Is.Not.Null.And.Not.Empty,
                    $"{resourcePath} has no player-visible value for '{key}'.");
            }

            Assert.That(valuesByKey["cocktail.stage"], Does.Contain("{0}"));
            Assert.That(valuesByKey["cocktail.result.final"], Does.Contain("{0}"));
            Assert.That(valuesByKey["cocktail.result.final"], Does.Contain("{1}"));
            Assert.That(valuesByKey["beerpong.result.final"], Does.Contain("{0}"));
            Assert.That(valuesByKey["beerpong.result.final"], Does.Contain("{1}"));
            Assert.That(valuesByKey["splitg.attempt"], Does.Contain("{0}"));
            Assert.That(valuesByKey["splitg.attempt"], Does.Contain("{1}"));
            Assert.That(valuesByKey["splitg.result.score"], Does.Contain("{0}"));
            Assert.That(valuesByKey["splitg.result.error"], Does.Contain("{0"));
        }

        [Test]
        public void LocalizedCatalogs_HaveMatchingKeySets()
        {
            Dictionary<string, string> russian = LoadValues("Localization/ru");
            Dictionary<string, string> english = LoadValues("Localization/en");

            CollectionAssert.AreEquivalent(russian.Keys, english.Keys);
        }

        private static Dictionary<string, string> LoadValues(string resourcePath)
        {
            TextAsset asset = Resources.Load<TextAsset>(resourcePath);
            Assert.That(
                asset,
                Is.Not.Null,
                $"Expected a TextAsset at Resources/{resourcePath}.json.");

            LocalizationCatalog catalog =
                JsonUtility.FromJson<LocalizationCatalog>(asset.text);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.entries, Is.Not.Null);

            var valuesByKey =
                new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < catalog.entries.Length; index++)
            {
                LocalizationEntry entry = catalog.entries[index];
                Assert.That(
                    entry,
                    Is.Not.Null,
                    $"{resourcePath} contains a null entry.");
                Assert.That(entry.key, Is.Not.Null.And.Not.Empty);
                Assert.That(
                    valuesByKey.ContainsKey(entry.key),
                    Is.False,
                    $"{resourcePath} contains duplicate key '{entry.key}'.");
                valuesByKey.Add(entry.key, entry.value);
            }

            return valuesByKey;
        }

        [Serializable]
        private sealed class LocalizationCatalog
        {
            public LocalizationEntry[] entries = Array.Empty<LocalizationEntry>();
        }

        [Serializable]
        private sealed class LocalizationEntry
        {
            public string key = string.Empty;
            public string value = string.Empty;
        }
    }
}
