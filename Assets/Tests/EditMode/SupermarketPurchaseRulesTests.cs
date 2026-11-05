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

        [TestCase(InventoryItemId.BeerCan, 6)]
        [TestCase(InventoryItemId.WineBottle, 22)]
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
                    expectedPrice,
                    0);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.CashBefore, Is.EqualTo(expectedPrice));
            Assert.That(result.CashAfter, Is.Zero);
            Assert.That(result.ItemCountBefore, Is.Zero);
            Assert.That(result.ItemCountAfter, Is.EqualTo(1));
        }

        [Test]
        public void Catalog_HasExactlySevenUniqueInventoryProducts()
        {
            Assert.That(SupermarketProductCatalog.Offers, Has.Count.EqualTo(7));
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

            Assert.That(ids, Does.Contain(InventoryItemId.BeerCan));
            Assert.That(ids, Does.Contain(InventoryItemId.WineBottle));
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
                    20,
                    1),
                SupermarketPurchaseStatus.InvalidSource);
            AssertFailure(
                SupermarketPurchaseRules.Evaluate(
                    "supermarket.test.not-offered",
                    InventoryItemId.OpenStewCan,
                    20,
                    1),
                SupermarketPurchaseStatus.NotOffered);
            AssertFailure(
                SupermarketPurchaseRules.Evaluate(
                    "supermarket.test.cash",
                    InventoryItemId.VodkaBottle,
                    27,
                    1),
                SupermarketPurchaseStatus.InsufficientFunds);
            AssertFailure(
                SupermarketPurchaseRules.Evaluate(
                    "supermarket.test.full",
                    InventoryItemId.ChickenEgg,
                    20,
                    9),
                SupermarketPurchaseStatus.InventoryFull);
        }

        [TestCase(InventoryItemId.BeerCan, 6)]
        [TestCase(InventoryItemId.WineBottle, 22)]
        [TestCase(InventoryItemId.ChickenEgg, 2)]
        [TestCase(InventoryItemId.VodkaBottle, 28)]
        [TestCase(InventoryItemId.ClosedStewCan, 11)]
        [TestCase(InventoryItemId.InstantNoodles, 3)]
        [TestCase(InventoryItemId.DayOldLoaf, 4)]
        public void SessionPurchase_IsRepeatableAtomicAndKeepsUniquePickupsSeparate(
            InventoryItemId itemId, int price)
        {
            const string source = "supermarket.test.reusable";
            const string uniqueSource = "world.test.unique";
            Assert.That(GameSessionState.TryCollectWorldItem(uniqueSource,
                InventoryItemId.OpenStewCan), Is.True);
            int cashBefore = GameSessionState.CashBalance;
            int stockEvents = 0;
            System.Action onStockChanged = () => stockEvents++;
            GameSessionState.WorldItemsChanged += onStockChanged;
            try
            {
                for (int purchase = 1; purchase <= 9; purchase++)
                {
                    SupermarketPurchaseResult result =
                        GameSessionState.TryPurchaseWorldItem(source, itemId);
                    Assert.That(result.Succeeded, Is.True);
                    Assert.That(GameSessionState.CashBalance,
                        Is.EqualTo(cashBefore - purchase * price));
                    Assert.That(GameSessionState.GetInventoryItemCount(itemId),
                        Is.EqualTo(purchase));
                    Assert.That(GameSessionState.IsWorldItemCollected(source), Is.False);
                }
                int cashAtCapacity = GameSessionState.CashBalance;
                SupermarketPurchaseResult full = GameSessionState.TryPurchaseWorldItem(source, itemId);
                AssertFailure(full, SupermarketPurchaseStatus.InventoryFull);
                Assert.That(GameSessionState.CashBalance, Is.EqualTo(cashAtCapacity));
                Assert.That(GameSessionState.GetInventoryItemCount(itemId), Is.EqualTo(9));
                Assert.That(GameSessionState.TryRemoveInventoryItem(itemId), Is.True);
                Assert.That(GameSessionState.TryPurchaseWorldItem(source, itemId).Succeeded, Is.True,
                    "The reusable offer must still work after making room in the inventory.");
                Assert.That(GameSessionState.CashBalance, Is.EqualTo(cashAtCapacity - price));
                Assert.That(stockEvents, Is.Zero,
                    "A replenishable purchase must not append a collected source or invalidate world stock.");
            }
            finally
            {
                GameSessionState.WorldItemsChanged -= onStockChanged;
            }

            Assert.That(GameSessionState.IsWorldItemCollected(uniqueSource), Is.True);
            Assert.That(GameSessionState.TryCollectWorldItem(uniqueSource,
                InventoryItemId.OpenStewCan), Is.False);
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.IsWorldItemCollected(uniqueSource), Is.False);
            Assert.That(GameSessionState.GetInventoryItemCount(itemId), Is.Zero);
            Assert.That(GameSessionState.CashBalance, Is.EqualTo(GameSessionState.DefaultCash));
        }

        [Test]
        public void RidgeKiosk_SellsOnlyItsThreeGoodsAndThePieFeedsTheHero()
        {
            const string source = "city.ridge.kiosk.pirozhok";
            const InventoryItemId pie = InventoryItemId.PotatoPirozhok;
            AssertFailure(GameSessionState.TryPurchaseWorldItem("supermarket", pie),
                SupermarketPurchaseStatus.NotOffered);
            AssertFailure(GameSessionState.TryPurchaseWorldItem(source, InventoryItemId.WineBottle, RetailStock.RidgeKiosk),
                SupermarketPurchaseStatus.NotOffered);
            AssertFailure(SupermarketPurchaseRules.Evaluate(source, pie, 4, 0, RetailStock.RidgeKiosk),
                SupermarketPurchaseStatus.InsufficientFunds);
            AssertFailure(SupermarketPurchaseRules.Evaluate(source, pie, 5, 9, RetailStock.RidgeKiosk),
                SupermarketPurchaseStatus.InventoryFull);

            int cash = GameSessionState.CashBalance;
            foreach (var item in new[] { InventoryItemId.BeerCan, InventoryItemId.VodkaBottle, pie })
            {
                Assert.That(GameSessionState.TryPurchaseWorldItem(source, item, RetailStock.RidgeKiosk).Succeeded, Is.True);
                Assert.That(GameSessionState.GetInventoryItemCount(item), Is.EqualTo(1));
            }
            Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash - 6 - 28 - 5));
            Assert.That(GameSessionState.TryConsumeInventoryItem(pie).Succeeded, Is.False);
            Assert.That(GameSessionState.GetInventoryItemCount(pie), Is.EqualTo(1),
                "A full hero keeps the pastry until hungry.");
            GameSessionState.UpdateNeeds(56, 40);
            Assert.That(GameSessionState.TryConsumeInventoryItem(pie).Succeeded, Is.True);
            Assert.That(GameSessionState.GetInventoryItemCount(pie), Is.Zero);
            Assert.That(GameSessionState.HungerLevel, Is.EqualTo(32));
            Assert.That(GameSessionState.StressLevel, Is.EqualTo(40));
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
