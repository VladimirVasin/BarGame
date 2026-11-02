using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class InventoryStateTests
    {
        [Test]
        public void StarterItems_AreStableUniqueAndOrdered()
        {
            var state = new InventoryState();

            state.ResetWithStarterItems();

            Assert.That(state.Items, Has.Count.EqualTo(2));
            Assert.That(
                state.Items[0].ItemId,
                Is.EqualTo(InventoryItemId.ApartmentKeys));
            Assert.That(
                state.Items[1].ItemId,
                Is.EqualTo(InventoryItemId.Lighter));
            Assert.That(state.Items[0].Count, Is.EqualTo(1));
            Assert.That(state.Items[1].Count, Is.EqualTo(1));
        }

        [Test]
        public void AddAndRemove_StacksWithoutChangingAcquisitionOrder()
        {
            var state = new InventoryState();
            state.ResetWithStarterItems();

            Assert.That(
                state.TryAdd(InventoryItemId.ChickenEgg, 2),
                Is.True);
            Assert.That(
                state.TryAdd(InventoryItemId.ChickenEgg, 3),
                Is.True);
            Assert.That(state.Items, Has.Count.EqualTo(3));
            Assert.That(
                state.Items[2].ItemId,
                Is.EqualTo(InventoryItemId.ChickenEgg));
            Assert.That(state.Items[2].Count, Is.EqualTo(5));

            Assert.That(
                state.TryRemove(InventoryItemId.ChickenEgg, 4),
                Is.True);
            Assert.That(
                state.GetCount(InventoryItemId.ChickenEgg),
                Is.EqualTo(1));
            Assert.That(
                state.TryRemove(InventoryItemId.ChickenEgg),
                Is.True);
            Assert.That(state.Items, Has.Count.EqualTo(2));
        }

        [Test]
        public void Equipment_RequiresOwnedClothingAndNeverConsumesIt()
        {
            var state = new InventoryState();
            state.ResetWithStarterItems();
            Assert.That(state.TrySetEquipped(InventoryItemId.Scarf, true), Is.False);
            Assert.That(state.TrySetEquipped(InventoryItemId.Lighter, true), Is.False);
            Assert.That(state.TryAdd(InventoryItemId.Scarf), Is.True);
            Assert.That(state.TryAdd(InventoryItemId.Scarf), Is.False);
            Assert.That(state.TrySetEquipped(InventoryItemId.Scarf, true), Is.True);
            Assert.That(state.IsEquipped(InventoryItemId.Scarf), Is.True);
            Assert.That(state.GetCount(InventoryItemId.Scarf), Is.EqualTo(1));
            Assert.That(state.TrySetEquipped(InventoryItemId.Scarf, false), Is.True);
            Assert.That(state.IsEquipped(InventoryItemId.Scarf), Is.False);
            Assert.That(state.GetCount(InventoryItemId.Scarf), Is.EqualTo(1));

            state.TrySetEquipped(InventoryItemId.Scarf, true);
            Assert.That(state.TryRemove(InventoryItemId.Scarf), Is.True);
            Assert.That(state.IsEquipped(InventoryItemId.Scarf), Is.False);
            state.TryAdd(InventoryItemId.Scarf);
            state.TrySetEquipped(InventoryItemId.Scarf, true);
            state.ResetWithStarterItems();
            Assert.That(state.IsEquipped(InventoryItemId.Scarf), Is.False);
            Assert.That(state.GetCount(InventoryItemId.Scarf), Is.Zero);
        }

        [TestCase(InventoryItemId.VodkaBottle, 9)]
        [TestCase(InventoryItemId.FirewoodLog, 1)]
        [TestCase(InventoryItemId.LodgeGroupPhotograph, 1)]
        public void InvalidOrOverflowingMutation_IsAtomic(
            InventoryItemId itemId, int maximumStack)
        {
            var state = new InventoryState();

            Assert.That(state.CanAdd(InventoryItemId.None), Is.False);
            Assert.That(state.TryAdd(InventoryItemId.None), Is.False);
            Assert.That(
                state.CanAdd(itemId, maximumStack),
                Is.True);
            Assert.That(
                state.TryAdd(itemId, maximumStack),
                Is.True);
            Assert.That(
                state.CanAdd(itemId),
                Is.False);
            Assert.That(
                state.TryAdd(itemId),
                Is.False);
            Assert.That(
                state.GetCount(itemId),
                Is.EqualTo(maximumStack));
            Assert.That(
                state.TryRemove(itemId, maximumStack + 1),
                Is.False);
            Assert.That(
                state.GetCount(itemId),
                Is.EqualTo(maximumStack));
            Assert.That(state.TryRemove(itemId), Is.True);
            Assert.That(state.TryAdd(itemId), Is.True);
            Assert.That(state.GetCount(itemId), Is.EqualTo(maximumStack));
        }

        [Test]
        public void VodkaBottles_PreserveIndependentFillsAcrossEquipmentAndGenericRemoval()
        {
            var state = new InventoryState();
            Assert.That(state.TryAdd(InventoryItemId.VodkaBottle, 2), Is.True);
            Assert.That(state.TrySetEquipped(InventoryItemId.VodkaBottle, true), Is.True);
            int openedId = state.VodkaBottleId;
            Assert.That(state.TryDrinkVodkaSip(openedId, out _), Is.True);
            Assert.That(state.TrySetEquipped(InventoryItemId.VodkaBottle, false), Is.True);
            Assert.That(state.TryAdd(InventoryItemId.VodkaBottle, 1, VodkaBottleState.RefrigeratorMl), Is.True);
            Assert.That(state.TryAdd(InventoryItemId.CombatCrowbar), Is.True);
            Assert.That(state.TrySetEquipped(InventoryItemId.CombatCrowbar, true), Is.True);
            Assert.That(state.TrySetEquipped(InventoryItemId.VodkaBottle, true), Is.True);
            Assert.That(state.IsEquipped(InventoryItemId.CombatCrowbar), Is.False);
            Assert.That(state.VodkaBottleId, Is.EqualTo(openedId));
            Assert.That(state.VodkaBottleRemainingMl, Is.EqualTo(467));
            for (int sip = 1; sip < VodkaBottleState.FullBottleSipCount; sip++)
                Assert.That(state.TryDrinkVodkaSip(openedId, out _), Is.True);
            Assert.That(state.GetCount(InventoryItemId.VodkaBottle), Is.EqualTo(2));
            Assert.That(state.IsEquipped(InventoryItemId.VodkaBottle), Is.False);
            Assert.That(state.VodkaBottleId, Is.Zero, "An empty bottle never auto-equips its successor.");
            Assert.That(state.TrySetEquipped(InventoryItemId.VodkaBottle, true), Is.True);
            int refrigeratorId = state.VodkaBottleId;
            Assert.That(refrigeratorId, Is.Not.EqualTo(openedId));
            Assert.That(state.VodkaBottleRemainingMl, Is.EqualTo(100));
            Assert.That(state.TryDrinkVodkaSip(openedId, out _), Is.False);
            Assert.That(state.TryRemove(InventoryItemId.VodkaBottle), Is.True);
            Assert.That(state.VodkaBottleId, Is.EqualTo(refrigeratorId), "Generic removal preserves the active bottle where possible.");
            Assert.That(state.VodkaBottleRemainingMl, Is.EqualTo(100));
            state.ResetWithStarterItems();
            Assert.That(state.VodkaBottleRemainingMl, Is.Zero);
            state.TryAdd(InventoryItemId.VodkaBottle);
            state.TrySetEquipped(InventoryItemId.VodkaBottle, true);
            Assert.That(state.VodkaBottleId, Is.Not.EqualTo(openedId).And.Not.EqualTo(refrigeratorId));
        }
    }
}
