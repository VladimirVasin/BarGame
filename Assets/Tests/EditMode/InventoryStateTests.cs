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
        public void InvalidOrOverflowingMutation_IsAtomic()
        {
            var state = new InventoryState();

            Assert.That(state.CanAdd(InventoryItemId.None), Is.False);
            Assert.That(state.TryAdd(InventoryItemId.None), Is.False);
            Assert.That(
                state.CanAdd(InventoryItemId.VodkaBottle, 9),
                Is.True);
            Assert.That(
                state.TryAdd(InventoryItemId.VodkaBottle, 9),
                Is.True);
            Assert.That(
                state.CanAdd(InventoryItemId.VodkaBottle),
                Is.False);
            Assert.That(
                state.TryAdd(InventoryItemId.VodkaBottle),
                Is.False);
            Assert.That(
                state.GetCount(InventoryItemId.VodkaBottle),
                Is.EqualTo(9));
            Assert.That(
                state.TryRemove(InventoryItemId.VodkaBottle, 10),
                Is.False);
            Assert.That(
                state.GetCount(InventoryItemId.VodkaBottle),
                Is.EqualTo(9));
        }
    }
}
