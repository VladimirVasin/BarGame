using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class StartMenuModelTests
    {
        [Test]
        public void Open_SelectsNewGameAndArmsTheCard()
        {
            var model = new StartMenuModel();

            model.Open();

            Assert.That(
                model.SelectedOption,
                Is.EqualTo(StartMenuOption.NewGame));
            Assert.That(model.IsCommitted, Is.False);
        }

        [Test]
        public void Navigation_WrapsBetweenTheTwoOptions()
        {
            var model = new StartMenuModel();
            model.Open();

            Assert.That(model.MoveSelection(-1), Is.True);
            Assert.That(
                model.SelectedOption,
                Is.EqualTo(StartMenuOption.Quit));
            Assert.That(model.MoveSelection(1), Is.True);
            Assert.That(
                model.SelectedOption,
                Is.EqualTo(StartMenuOption.NewGame));
            Assert.That(model.MoveSelection(0), Is.False);
        }

        [Test]
        public void Confirm_StartsTheRunOnceAndThenRefusesEverything()
        {
            var model = new StartMenuModel();
            model.Open();

            Assert.That(
                model.Confirm(),
                Is.EqualTo(StartMenuAction.NewGame));
            Assert.That(model.IsCommitted, Is.True);

            // A mouse click does not consult the input policy, so a second
            // activation must die here rather than ask for a second trip.
            Assert.That(
                model.Confirm(),
                Is.EqualTo(StartMenuAction.None));
            Assert.That(model.MoveSelection(1), Is.False);
            Assert.That(
                model.SelectOption(StartMenuOption.Quit),
                Is.False);
            Assert.That(
                model.SelectedOption,
                Is.EqualTo(StartMenuOption.NewGame));
        }

        [Test]
        public void Confirm_OnQuitAsksToQuit()
        {
            var model = new StartMenuModel();
            model.Open();

            Assert.That(
                model.SelectOption(StartMenuOption.Quit),
                Is.True);
            Assert.That(
                model.Confirm(),
                Is.EqualTo(StartMenuAction.Quit));
        }

        [Test]
        public void Reopening_ReArmsACardWhoseTripWasRefused()
        {
            var model = new StartMenuModel();
            model.Open();
            model.Confirm();

            model.Open();

            Assert.That(model.IsCommitted, Is.False);
            Assert.That(
                model.SelectedOption,
                Is.EqualTo(StartMenuOption.NewGame));
            Assert.That(
                model.Confirm(),
                Is.EqualTo(StartMenuAction.NewGame));
        }
    }
}
