using System.Collections.Generic;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class SupermarketPurchaseRulesTests
    {
        [SetUp]
        public void SetUp()
        {
            GameSessionState.BeginNewGame();
        }

        [TearDown]
        public void TearDown()
        {
            GameSessionState.BeginNewGame();
        }

        [TestCase(InventoryItemId.ChickenEgg, 2)]
        [TestCase(InventoryItemId.VodkaBottle, 28)]
        [TestCase(InventoryItemId.ClosedStewCan, 11)]
        [TestCase(InventoryItemId.InstantNoodles, 3)]
        [TestCase(InventoryItemId.DayOldLoaf, 4)]
        public void CatalogOffer_WithExactFunds_ProducesOneItemPurchase(
            InventoryItemId itemId,
            int expectedPrice)
        {
            SupermarketProductOffer offer =
                SupermarketProductCatalog.GetOffer(itemId);

            Assert.That(offer.Price, Is.EqualTo(expectedPrice));
            Assert.That(
                offer.NameLocalizationKey,
                Is.EqualTo(
                    InventoryItemCatalog.Get(itemId)
                        .NameLocalizationKey));
            Assert.That(
                offer.DescriptionLocalizationKey,
                Is.EqualTo(
                    InventoryItemCatalog.Get(itemId)
                        .DescriptionLocalizationKey));

            SupermarketPurchaseResult result =
                SupermarketPurchaseRules.Evaluate(
                    "supermarket.test.offer",
                    itemId,
                    false,
                    expectedPrice,
                    0);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.CashBefore, Is.EqualTo(expectedPrice));
            Assert.That(result.CashAfter, Is.Zero);
            Assert.That(result.ItemCountBefore, Is.Zero);
            Assert.That(result.ItemCountAfter, Is.EqualTo(1));
        }

        [Test]
        public void Catalog_HasExactlyFiveUniqueInventoryProducts()
        {
            Assert.That(SupermarketProductCatalog.Offers, Has.Count.EqualTo(5));
            var ids = new HashSet<InventoryItemId>();
            for (int index = 0;
                 index < SupermarketProductCatalog.Offers.Count;
                 index++)
            {
                SupermarketProductOffer offer =
                    SupermarketProductCatalog.Offers[index];
                Assert.That(ids.Add(offer.ItemId), Is.True);
                Assert.That(offer.Price, Is.GreaterThan(0));
                Assert.That(
                    InventoryItemCatalog.TryGet(offer.ItemId, out _),
                    Is.True);
            }

            Assert.That(ids, Does.Contain(InventoryItemId.ChickenEgg));
            Assert.That(ids, Does.Contain(InventoryItemId.VodkaBottle));
            Assert.That(ids, Does.Contain(InventoryItemId.ClosedStewCan));
            Assert.That(ids, Does.Contain(InventoryItemId.InstantNoodles));
            Assert.That(ids, Does.Contain(InventoryItemId.DayOldLoaf));
            Assert.That(ids, Has.No.Member(InventoryItemId.OpenStewCan));
        }

        [Test]
        public void RuleFailures_LeaveCashAndItemCountUnchanged()
        {
            AssertFailure(
                SupermarketPurchaseRules.Evaluate(
                    " ",
                    InventoryItemId.ChickenEgg,
                    false,
                    20,
                    1),
                SupermarketPurchaseStatus.InvalidSource);
            AssertFailure(
                SupermarketPurchaseRules.Evaluate(
                    "supermarket.test.not-offered",
                    InventoryItemId.OpenStewCan,
                    false,
                    20,
                    1),
                SupermarketPurchaseStatus.NotOffered);
            AssertFailure(
                SupermarketPurchaseRules.Evaluate(
                    "supermarket.test.sold",
                    InventoryItemId.ChickenEgg,
                    true,
                    20,
                    1),
                SupermarketPurchaseStatus.AlreadyPurchased);
            AssertFailure(
                SupermarketPurchaseRules.Evaluate(
                    "supermarket.test.cash",
                    InventoryItemId.VodkaBottle,
                    false,
                    27,
                    1),
                SupermarketPurchaseStatus.InsufficientFunds);
            AssertFailure(
                SupermarketPurchaseRules.Evaluate(
                    "supermarket.test.full",
                    InventoryItemId.ChickenEgg,
                    false,
                    20,
                    9),
                SupermarketPurchaseStatus.InventoryFull);
        }

        [Test]
        public void SessionPurchase_IsAtomicAndPersistsSourceUntilNewGame()
        {
            const string purchasedSource = "supermarket.test.egg.1";
            int cashBefore = GameSessionState.CashBalance;

            SupermarketPurchaseResult purchased =
                GameSessionState.TryPurchaseWorldItem(
                    purchasedSource,
                    InventoryItemId.ChickenEgg);

            Assert.That(purchased.Succeeded, Is.True);
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(cashBefore - 2));
            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.ChickenEgg),
                Is.EqualTo(1));
            Assert.That(
                GameSessionState.IsWorldItemCollected(purchasedSource),
                Is.True);

            SupermarketPurchaseResult duplicate =
                GameSessionState.TryPurchaseWorldItem(
                    purchasedSource,
                    InventoryItemId.ChickenEgg);
            Assert.That(
                duplicate.Status,
                Is.EqualTo(
                    SupermarketPurchaseStatus.AlreadyPurchased));
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(cashBefore - 2));
            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.ChickenEgg),
                Is.EqualTo(1));

            Assert.That(
                GameSessionState.TryAddInventoryItem(
                    InventoryItemId.ChickenEgg,
                    8),
                Is.True);
            const string fullSource = "supermarket.test.egg.full";
            SupermarketPurchaseResult full =
                GameSessionState.TryPurchaseWorldItem(
                    fullSource,
                    InventoryItemId.ChickenEgg);
            Assert.That(
                full.Status,
                Is.EqualTo(SupermarketPurchaseStatus.InventoryFull));
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(cashBefore - 2));
            Assert.That(
                GameSessionState.IsWorldItemCollected(fullSource),
                Is.False);

            GameSessionState.BeginNewGame();
            Assert.That(
                GameSessionState.IsWorldItemCollected(purchasedSource),
                Is.False);
            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.ChickenEgg),
                Is.Zero);
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(GameSessionState.DefaultCash));
        }

        private static void AssertFailure(
            SupermarketPurchaseResult result,
            SupermarketPurchaseStatus expectedStatus)
        {
            Assert.That(result.Status, Is.EqualTo(expectedStatus));
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.CashAfter, Is.EqualTo(result.CashBefore));
            Assert.That(
                result.ItemCountAfter,
                Is.EqualTo(result.ItemCountBefore));
        }
    }
}
