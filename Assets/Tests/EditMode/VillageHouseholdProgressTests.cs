using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class VillageHouseholdProgressTests
    {
        [TestCase(0, 1)]
        [TestCase(1, 0)]
        public void Firewood_UniqueLogsAndEitherDeliveryOrderConserveTheStock(int firstBasket, int firstStand)
        {
            var state = new VillageHouseholdProgress();
            int secondBasket = 1 - firstBasket;
            Assert.That(state.TryDeliverBasket(firstBasket, firstStand), Is.False, "Empty baskets are not finished deliveries.");
            for (int log = 0; log < 3; log++)
            {
                Assert.That(state.TryLoadLog(firstBasket, log), Is.True);
                Assert.That(state.TryLoadLog(firstBasket, log), Is.False, "Repeated contact cannot duplicate a log.");
                Assert.That(state.TryLoadLog(secondBasket, log), Is.False, "A log cannot belong to two baskets.");
            }
            Assert.That(state.TryLoadLog(firstBasket, 3), Is.False, "The first basket is full.");
            Assert.That(state.LooseLogCount, Is.EqualTo(3));
            Assert.That(state.GetBasketDeliveryStand(firstBasket), Is.EqualTo(-1), "Loading or transit does not commit a delivery.");
            for (int log = 3; log < 6; log++) Assert.That(state.TryLoadLog(secondBasket, log), Is.True);
            Assert.That(state.TryDeliverBasket(firstBasket, firstStand), Is.True);
            Assert.That(state.TryDeliverBasket(firstBasket, 1 - firstStand), Is.False);
            Assert.That(state.TryDeliverBasket(secondBasket, firstStand), Is.False, "Occupied supports cannot accept another basket.");
            Assert.That(state.GetBasketDeliveryStand(secondBasket), Is.EqualTo(-1));
            Assert.That(state.TryDeliverBasket(secondBasket, 1 - firstStand), Is.True);
            Assert.That(state.GetBasketAtStand(firstStand), Is.EqualTo(firstBasket));
            Assert.That(state.GetBasketDeliveryStand(firstBasket), Is.EqualTo(firstStand));
            Assert.That(state.DeliveredBasketCount, Is.EqualTo(2));
            Assert.That(state.LooseLogCount, Is.Zero);
            Assert.That(state.GetBasketLogCount(0) + state.GetBasketLogCount(1), Is.EqualTo(6));
            for (int log = 0; log < 6; log++)
                Assert.That(state.GetLogBasket(log), Is.EqualTo(log < 3 ? firstBasket : secondBasket));
        }

        [TestCase(VillageHouseholdProgress.PorchClearingId)]
        [TestCase(VillageHouseholdProgress.ChapelClearingId)]
        [TestCase(VillageHouseholdProgress.StationEdgeClearingId)]
        public void Clearing_OnlyTheExpectedNextStageCommitsAndCompletionIsFinite(string id)
        {
            var state = new VillageHouseholdProgress();
            Assert.That(state.TryAdvanceClearing(id, 1), Is.False);
            Assert.That(state.GetClearingStage(id), Is.Zero);
            for (int stage = 0; stage < VillageHouseholdProgress.ClearingStageCount; stage++)
            {
                Assert.That(state.TryAdvanceClearing(id, stage), Is.True);
                Assert.That(state.TryAdvanceClearing(id, stage), Is.False);
                Assert.That(state.GetClearingStage(id), Is.EqualTo(stage + 1));
            }
            Assert.That(state.TryAdvanceClearing(id, VillageHouseholdProgress.ClearingStageCount), Is.False);
            string other = id == VillageHouseholdProgress.PorchClearingId
                ? VillageHouseholdProgress.ChapelClearingId : VillageHouseholdProgress.PorchClearingId;
            Assert.That(state.GetClearingStage(other), Is.Zero, "A work patch cannot clear a different place.");
        }

        [TestCase(-1, 0)]
        [TestCase(2, 0)]
        [TestCase(0, -1)]
        [TestCase(0, 6)]
        public void InvalidTransfer_DoesNotConsumeAnyStock(int basket, int log)
        {
            var state = new VillageHouseholdProgress();
            Assert.That(state.TryLoadLog(basket, log), Is.False);
            Assert.That(state.LooseLogCount, Is.EqualTo(6));
            Assert.That(state.DeliveredBasketCount, Is.Zero);
        }

        [Test]
        public void CompletedResults_AreIdempotentAndResetTogetherForANewSession()
        {
            var state = new VillageHouseholdProgress();
            for (int log = 0; log < 3; log++) state.TryLoadLog(0, log);
            Assert.That(state.TryDeliverBasket(0, 1), Is.True);
            Assert.That(state.TryCompleteChairRepair(), Is.True);
            Assert.That(state.TryCompleteChairRepair(), Is.False);
            Assert.That(state.TryCompleteWaterFetch(0), Is.True);
            Assert.That(state.TryCompleteWaterFetch(0), Is.False);
            Assert.That(state.TryCompleteWaterFetch(1), Is.True);
            Assert.That(state.CompletedWaterVisits, Is.EqualTo(2));
            Assert.That(state.WaterFetched, Is.True);
            Assert.That(state.TryAdvanceClearing(VillageHouseholdProgress.PorchClearingId, 0), Is.True);
            state.Reset();
            Assert.That(state.ChairFixed || state.WaterFetched, Is.False);
            Assert.That(state.CompletedWaterVisits, Is.Zero);
            Assert.That(state.DeliveredBasketCount, Is.Zero);
            Assert.That(state.LooseLogCount, Is.EqualTo(6));
            Assert.That(state.GetClearingStage(VillageHouseholdProgress.PorchClearingId), Is.Zero);
            for (int basket = 0; basket < 2; basket++)
            {
                Assert.That(state.GetBasketDeliveryStand(basket), Is.EqualTo(-1));
                Assert.That(state.GetBasketLogCount(basket), Is.Zero);
            }
        }
    }
}
