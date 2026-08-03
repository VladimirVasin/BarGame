using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class InventoryMenuModelTests
    {
        [Test]
        public void Open_ClampsSelectionAndLeavesExaminePage()
        {
            var model = new InventoryMenuModel();

            model.Open(3);
            model.SelectItem(2, 3);
            model.BeginExamine(3);
            model.Open(1);

            Assert.That(model.SelectedItemIndex, Is.Zero);
            Assert.That(model.IsExamining, Is.False);
        }

        [Test]
        public void Navigation_WrapsWithGridSizedDeltas()
        {
            var model = new InventoryMenuModel();
            model.Open(7);

            Assert.That(model.MoveSelection(-1, 7), Is.True);
            Assert.That(model.SelectedItemIndex, Is.EqualTo(6));
            Assert.That(model.MoveSelection(5, 7), Is.True);
            Assert.That(model.SelectedItemIndex, Is.EqualTo(4));
        }

        [Test]
        public void EmptyInventory_RejectsSelectionAndExamine()
        {
            var model = new InventoryMenuModel();
            model.Open(0);

            Assert.That(model.SelectedItemIndex, Is.EqualTo(-1));
            Assert.That(model.MoveSelection(1, 0), Is.False);
            Assert.That(model.BeginExamine(0), Is.False);
            Assert.That(model.IsExamining, Is.False);
        }
    }
}
