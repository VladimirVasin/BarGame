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
            "interaction.enter_supermarket",
            "interaction.exit_supermarket",
            "interaction.browse_supermarket_shelf",
            "interaction.enter_home",
            "interaction.exit_home",
            "interaction.enter_building",
            "interaction.exit_building",
            "interaction.enter_apartment",
            "interaction.exit_apartment",
            "interaction.sleep",
            "interaction.wake",
            "interaction.smoke",
            "interaction.stop_smoking",
            "interaction.open_refrigerator",
            "interaction.close_refrigerator",
            "home.refrigerator.item.vodka.name",
            "home.refrigerator.item.vodka.description",
            "home.refrigerator.item.egg.name",
            "home.refrigerator.item.egg.description",
            "home.refrigerator.item.stew_can.name",
            "home.refrigerator.item.stew_can.description",
            "home.refrigerator.action.take",
            "home.refrigerator.action.use",
            "home.refrigerator.action.back",
            "home.refrigerator.action.unavailable",
            "inventory.status",
            "inventory.status.intoxication",
            "inventory.status.hunger",
            "inventory.status.stress",
            "inventory.status.fatigue",
            "inventory.items",
            "inventory.selected_item",
            "inventory.command",
            "inventory.cash",
            "inventory.time",
            "inventory.empty",
            "inventory.action.use",
            "inventory.action.eat",
            "inventory.action.drink",
            "inventory.action.examine",
            "inventory.action.close",
            "inventory.action.back",
            "inventory.use.success.food",
            "inventory.use.success.alcohol",
            "inventory.use.failure.not_consumable",
            "inventory.use.failure.missing_item",
            "inventory.use.failure.no_effect",
            "inventory.use.failure.maximum_intoxication",
            "inventory.use.failure.generic",
            "inventory.item.apartment_keys.name",
            "inventory.item.apartment_keys.description",
            "inventory.item.lighter.name",
            "inventory.item.lighter.description",
            "inventory.item.vodka_bottle.name",
            "inventory.item.vodka_bottle.description",
            "inventory.item.chicken_egg.name",
            "inventory.item.chicken_egg.description",
            "inventory.item.open_stew_can.name",
            "inventory.item.open_stew_can.description",
            "inventory.item.closed_stew_can.name",
            "inventory.item.closed_stew_can.description",
            "inventory.item.instant_noodles.name",
            "inventory.item.instant_noodles.description",
            "inventory.item.day_old_loaf.name",
            "inventory.item.day_old_loaf.description",
            "supermarket.shop.title",
            "supermarket.shop.balance",
            "supermarket.shop.price",
            "supermarket.shop.buy",
            "supermarket.shop.back",
            "supermarket.failure.invalid_source",
            "supermarket.failure.not_offered",
            "supermarket.failure.already_purchased",
            "supermarket.failure.insufficient_funds",
            "supermarket.failure.inventory_full",
            "interaction.cat",
            "stairwell.cat.placeholder",
            "stairwell.cat.feed.confirm",
            "stairwell.cat.feed.missing",
            "interaction.order_drinks",
            "interaction.buy_drink",
            "interaction.play_beer_pong",
            "interaction.play_split_the_g",
            "interaction.play_tincture_match",
            "pause.title",
            "pause.resume",
            "pause.restart",
            "pause.quit",
            "pause.restart_confirm",
            "pause.quit_confirm",
            "pause.yes",
            "pause.no",
            "common.yes",
            "common.no",
            "interaction.choice.talk",
            "interaction.choice.interact",
            "drinking.intoxication",
            "intoxication.stage.sober",
            "intoxication.stage.light_buzz",
            "intoxication.stage.tipsy",
            "intoxication.stage.drunk",
            "intoxication.stage.unsteady",
            "intoxication.stage.very_drunk",
            "balance.warning",
            "balance.hold",
            "cocktail.title",
            "cocktail.stage",
            "cocktail.choose_base",
            "cocktail.choose_base_hint",
            "cocktail.mix_hint",
            "cocktail.serve",
            "cocktail.finish",
            "cocktail.score.current",
            "cocktail.score.total",
            "cocktail.feedback.good",
            "cocktail.feedback.bad",
            "cocktail.result.round",
            "cocktail.result.final",
            "cocktail.result.max_intoxication",
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
            "beerpong.feedback.clean",
            "beerpong.feedback.bank",
            "beerpong.feedback.rim",
            "beerpong.feedback.bounce",
            "beerpong.feedback.miss",
            "beerpong.result.cleared",
            "beerpong.result.out_of_throws",
            "beerpong.result.max_intoxication",
            "beerpong.result.final",
            "beerpong.finish",
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
            "tincture.title",
            "tincture.moves",
            "tincture.score",
            "tincture.combo",
            "tincture.intoxication",
            "tincture.selected",
            "tincture.selected.none",
            "tincture.invalid_swap",
            "tincture.reshuffling",
            "tincture.final",
            "tincture.result.score",
            "tincture.result.moves",
            "tincture.result.combo",
            "tincture.result.moonshine",
            "tincture.continue",
            "tincture.xxx.warning",
            "tincture.flavor.cherry",
            "tincture.flavor.seabuckthorn",
            "tincture.flavor.blueberry",
            "tincture.flavor.mint",
            "tincture.flavor.horseradish",
            "tincture.flavor.moonshine",
            "tincture.rank.miss",
            "tincture.rank.close",
            "tincture.rank.good",
            "tincture.rank.excellent",
            "tincture.rank.perfect",
            "drink.light_beer",
            "drink.dark_beer",
            "drink.white_wine",
            "drink.red_wine",
            "drink.vodka",
            "drink.pepper_vodka",
            "drink.cognac_vs",
            "drink.cognac_vsop",
            "drink.water",
            "drink_shop.title",
            "drink_shop.balance",
            "drink_shop.price",
            "drink_shop.intoxication_gain",
            "drink_shop.preview",
            "drink_shop.buy",
            "drink_shop.cancel",
            "drink_shop.exit",
            "drink_shop.serving",
            "drink_shop.pouring",
            "drink_shop.drinking",
            "drink_shop.vessel_return",
            "drink_shop.failure.insufficient_funds",
            "drink_shop.failure.maximum_intoxication",
            "drink_shop.failure.not_offered",
            "map.title",
            "map.route_title",
            "map.route_empty",
            "map.bar_name",
            "map.player",
            "map.home",
            "map.supermarket",
            "map.bus.route",
            "map.bus.stop",
            "map.bus.stop_legend",
            "bus.stop.default_coastal.industrial",
            "bus.stop.default_coastal.nightlife",
            "bus.stop.default_coastal.residential",
            "bus.stop.default_coastal.old_town",
            "map.clear",
            "map.visited_count",
            "map.distance",
            "map.object",
            "map.teleport.title",
            "map.teleport.select",
            "map.teleport.question",
            "map.poi.title",
            "map.poi.old_town_waterworks_court",
            "map.poi.residential_drying_yard",
            "map.poi.industrial_weighbridge",
            "map.poi.nightlife_last_route_island",
            "map.district.old_town",
            "map.district.residential",
            "map.district.industrial",
            "map.district.nightlife",
            "map.district.central_park",
            "map.district.north_waterfront",
            "map.district.lake",
            "map.district.cemetery",
            "debug.minigames.title",
            "debug.minigames.hint",
            "debug.minigames.empty",
            "debug.minigames.unavailable",
            "debug.teleport.disabled",
            "debug.teleport.enabled",
            "debug.minigame.cocktail",
            "debug.minigame.beer_pong",
            "debug.minigame.split_the_g",
            "debug.minigame.tincture_match"
        };

        private static readonly string[] RetiredControlHintKeys =
        {
            "opening.controls",
            "home.refrigerator.controls.browse",
            "home.refrigerator.controls.inspect",
            "cocktail.controls.choose",
            "cocktail.controls.add",
            "cocktail.controls.serve",
            "cocktail.controls.back",
            "beerpong.controls.aim",
            "beerpong.controls.throw",
            "beerpong.controls.cancel",
            "beerpong.result.continue",
            "splitg.controls",
            "tincture.controls",
            "drink_shop.controls",
            "map.open_hint",
            "map.instructions",
            "debug.minigames.controls"
        };

        private static readonly string[] RetiredPointOfInterestKeys =
        {
            "map.poi.old_town_palimpsest_house",
            "map.poi.residential_corridor_gallery",
            "map.poi.industrial_shift_gatehouse",
            "map.poi.nightlife_glass_block_hall"
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
            Assert.That(valuesByKey["tincture.moves"], Does.Contain("{0}"));
            Assert.That(valuesByKey["tincture.score"], Does.Contain("{0}"));
            Assert.That(valuesByKey["tincture.combo"], Does.Contain("{0}"));
            Assert.That(
                valuesByKey["tincture.intoxication"],
                Does.Contain("{0}"));
            Assert.That(valuesByKey["tincture.selected"], Does.Contain("{0}"));
            Assert.That(
                valuesByKey["tincture.result.score"],
                Does.Contain("{0}"));
            Assert.That(
                valuesByKey["tincture.result.moves"],
                Does.Contain("{0}"));
            Assert.That(
                valuesByKey["tincture.result.combo"],
                Does.Contain("{0}"));
            Assert.That(
                valuesByKey["tincture.result.moonshine"],
                Does.Contain("{0}"));
            Assert.That(
                valuesByKey["drink_shop.balance"],
                Does.Contain("{0}"));
            Assert.That(
                valuesByKey["drink_shop.price"],
                Does.Contain("{0}"));
            Assert.That(
                valuesByKey["drink_shop.intoxication_gain"],
                Does.Contain("{0}"));
            Assert.That(
                valuesByKey["drink_shop.preview"],
                Does.Contain("{0}"));
            Assert.That(
                valuesByKey["drink_shop.preview"],
                Does.Contain("{1}"));
            Assert.That(
                valuesByKey["drink_shop.preview"],
                Does.Contain("{2}"));
            Assert.That(
                valuesByKey["drink_shop.preview"],
                Does.Contain("{3}"));
            Assert.That(valuesByKey["inventory.cash"], Does.Contain("$"));
            Assert.That(
                valuesByKey["inventory.time"],
                Does.Contain("{0}"));
            Assert.That(
                valuesByKey["inventory.action.eat"],
                Does.Contain("{0}"));
            Assert.That(
                valuesByKey["inventory.action.drink"],
                Does.Contain("{0}"));
            Assert.That(
                valuesByKey["inventory.use.success.food"],
                Does.Contain("{0}"));
            Assert.That(
                valuesByKey["inventory.use.success.alcohol"],
                Does.Contain("{0}"));
            Assert.That(
                valuesByKey["inventory.use.success.alcohol"],
                Does.Contain("{1}"));
            Assert.That(
                valuesByKey["supermarket.shop.balance"],
                Does.Contain("{0}"));
            Assert.That(
                valuesByKey["supermarket.shop.price"],
                Does.Contain("{0}"));
            Assert.That(
                valuesByKey["inventory.cash"],
                Does.Not.Contain("₽"));
        }

        [TestCase("Localization/ru")]
        [TestCase("Localization/en")]
        public void Catalog_DoesNotContainRetiredControlHints(
            string resourcePath)
        {
            Dictionary<string, string> valuesByKey = LoadValues(resourcePath);

            for (int index = 0;
                 index < RetiredControlHintKeys.Length;
                 index++)
            {
                Assert.That(
                    valuesByKey.ContainsKey(RetiredControlHintKeys[index]),
                    Is.False,
                    $"{resourcePath} still contains control hint " +
                    $"'{RetiredControlHintKeys[index]}'.");
            }
        }

        [TestCase("Localization/ru")]
        [TestCase("Localization/en")]
        public void Catalog_DoesNotContainRetiredFacadePointOfInterestKeys(
            string resourcePath)
        {
            Dictionary<string, string> valuesByKey = LoadValues(resourcePath);

            for (int index = 0;
                 index < RetiredPointOfInterestKeys.Length;
                 index++)
            {
                Assert.That(
                    valuesByKey.ContainsKey(
                        RetiredPointOfInterestKeys[index]),
                    Is.False,
                    $"{resourcePath} still contains retired facade POI " +
                    $"'{RetiredPointOfInterestKeys[index]}'.");
            }
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
