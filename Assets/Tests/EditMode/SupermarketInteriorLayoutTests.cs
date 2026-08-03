using System.Collections.Generic;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class SupermarketInteriorLayoutTests
    {
        [Test]
        public void Generate_CreatesStableReachableThreeShelfInterior()
        {
            SupermarketInteriorLayoutPlan first =
                SupermarketInteriorLayoutPlanner.Generate(
                    20260804,
                    "supermarket-test");
            SupermarketInteriorLayoutPlan second =
                SupermarketInteriorLayoutPlanner.Generate(
                    20260804,
                    "supermarket-test");

            Assert.That(first.RoomSize.x, Is.EqualTo(16f));
            Assert.That(first.RoomSize.y, Is.EqualTo(11f));
            Assert.That(first.RoomHeight, Is.EqualTo(3.6f));
            Assert.That(first.Shelves, Has.Count.EqualTo(3));
            Assert.That(first.Paths, Has.Count.EqualTo(4));
            Assert.That(first.Cashier.VisualVariant, Is.EqualTo(1));
            Assert.DoesNotThrow(
                () => SupermarketInteriorLayoutValidator
                    .ValidateOrThrow(first));

            var sourceIds = new HashSet<string>();
            var itemIds = new HashSet<InventoryItemId>();
            int productCount = 0;
            for (int shelfIndex = 0;
                 shelfIndex < first.Shelves.Count;
                 shelfIndex++)
            {
                SupermarketShelfPlan shelf = first.Shelves[shelfIndex];
                Assert.That(shelf.Products, Is.Not.Empty, shelf.Id);
                Assert.That(
                    second.Shelves[shelfIndex].Id,
                    Is.EqualTo(shelf.Id));
                Assert.That(
                    second.Shelves[shelfIndex].CameraPosition,
                    Is.EqualTo(shelf.CameraPosition));
                for (int productIndex = 0;
                     productIndex < shelf.Products.Count;
                     productIndex++)
                {
                    SupermarketProductSlotPlan product =
                        shelf.Products[productIndex];
                    productCount++;
                    Assert.That(sourceIds.Add(product.SourceId), Is.True);
                    Assert.That(itemIds.Add(product.ItemId), Is.True);
                    Assert.That(
                        second.Shelves[shelfIndex]
                            .Products[productIndex].SourceId,
                        Is.EqualTo(product.SourceId));
                }
            }

            Assert.That(productCount, Is.EqualTo(5));
            CollectionAssert.AreEquivalent(
                new[]
                {
                    InventoryItemId.ChickenEgg,
                    InventoryItemId.VodkaBottle,
                    InventoryItemId.ClosedStewCan,
                    InventoryItemId.InstantNoodles,
                    InventoryItemId.DayOldLoaf
                },
                itemIds);
        }
    }
}
