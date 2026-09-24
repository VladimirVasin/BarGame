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
    }
}
