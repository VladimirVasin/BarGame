using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class InventoryConsumableCatalogTests
    {
        private static readonly object[] expectedDefinitions =
        {
            new object[]
            {
                InventoryItemId.VodkaBottle,
                InventoryConsumableKind.Alcohol,
                0,
                0,
                DrinkId.Vodka,
                4d,
                48
            },
            new object[]
            {
                InventoryItemId.ChickenEgg,
                InventoryConsumableKind.Food,
                10,
                20,
                DrinkId.None,
                0d,
                0
            },
            new object[]
            {
                InventoryItemId.OpenStewCan,
                InventoryConsumableKind.Food,
                35,
                20,
                DrinkId.None,
                0d,
                0
            },
            new object[]
            {
                InventoryItemId.ClosedStewCan,
                InventoryConsumableKind.Food,
                35,
                20,
                DrinkId.None,
                0d,
                0
            },
            new object[]
            {
                InventoryItemId.InstantNoodles,
                InventoryConsumableKind.Food,
                22,
                20,
                DrinkId.None,
                0d,
                0
            },
            new object[]
            {
                InventoryItemId.DayOldLoaf,
                InventoryConsumableKind.Food,
                18,
                20,
                DrinkId.None,
                0d,
                0
            }
        };

        [Test]
        public void All_ExposeEveryConsumableExactlyOnce()
        {
            IReadOnlyList<InventoryConsumableDefinition> definitions =
                InventoryConsumableCatalog.All;

            Assert.That(
                definitions,
                Has.Count.EqualTo(expectedDefinitions.Length));
            var uniqueItems = new HashSet<InventoryItemId>();
            for (int index = 0; index < definitions.Count; index++)
            {
                InventoryConsumableDefinition definition =
                    definitions[index];
                AssertDefinition(
                    definition,
                    (object[])expectedDefinitions[index]);
                Assert.That(uniqueItems.Add(definition.ItemId), Is.True);
                Assert.That(
                    InventoryItemCatalog.Get(definition.ItemId).Category,
                    Is.EqualTo(InventoryItemCategory.Consumable));
            }
        }

        [TestCaseSource(nameof(expectedDefinitions))]
        public void Lookup_ReturnsMatchingDefinition(
            InventoryItemId itemId,
            InventoryConsumableKind kind,
            int hungerRelief,
            int minimumHungerAfterUse,
            DrinkId drinkId,
            double servings,
            int stressRelief)
        {
            Assert.That(
                InventoryConsumableCatalog.TryGet(
                    itemId,
                    out InventoryConsumableDefinition definition),
                Is.True);
            AssertDefinition(
                definition,
                new object[]
                {
                    itemId,
                    kind,
                    hungerRelief,
                    minimumHungerAfterUse,
                    drinkId,
                    servings,
                    stressRelief
                });
            Assert.That(
                InventoryConsumableCatalog.Get(itemId),
                Is.EqualTo(definition));
        }

        [TestCase(InventoryItemId.None)]
        [TestCase(InventoryItemId.ApartmentKeys)]
        [TestCase(InventoryItemId.Lighter)]
        [TestCase((InventoryItemId)999)]
        public void Lookup_RejectsNonConsumableItems(InventoryItemId itemId)
        {
            Assert.That(
                InventoryConsumableCatalog.TryGet(
                    itemId,
                    out InventoryConsumableDefinition definition),
                Is.False);
            Assert.That(
                definition,
                Is.EqualTo(default(InventoryConsumableDefinition)));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => InventoryConsumableCatalog.Get(itemId));
        }

        private static void AssertDefinition(
            InventoryConsumableDefinition definition,
            object[] expected)
        {
            Assert.That(
                definition.ItemId,
                Is.EqualTo((InventoryItemId)expected[0]));
            Assert.That(
                definition.Kind,
                Is.EqualTo((InventoryConsumableKind)expected[1]));
            Assert.That(definition.HungerRelief, Is.EqualTo((int)expected[2]));
            Assert.That(
                definition.MinimumHungerAfterUse,
                Is.EqualTo((int)expected[3]));
            Assert.That(
                definition.DrinkId,
                Is.EqualTo((DrinkId)expected[4]));
            Assert.That(definition.Servings, Is.EqualTo((double)expected[5]));
            Assert.That(definition.StressRelief, Is.EqualTo((int)expected[6]));
        }
    }
}
