using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class PauseMenuModelTests
    {
        [Test]
        public void Open_SelectsResumeAndMainPage()
        {
            var model = new PauseMenuModel();

            model.Open();

            Assert.That(model.Page, Is.EqualTo(PauseMenuPage.Main));
            Assert.That(
                model.SelectedOption,
                Is.EqualTo(PauseMenuOption.Resume));
            Assert.That(model.ConfirmationYesSelected, Is.False);
        }

        [Test]
        public void MainNavigation_WrapsInBothDirections()
        {
            var model = new PauseMenuModel();
            model.Open();

            Assert.That(model.MoveSelection(-1), Is.True);
            Assert.That(
                model.SelectedOption,
                Is.EqualTo(PauseMenuOption.Quit));
            Assert.That(model.MoveSelection(1), Is.True);
            Assert.That(
                model.SelectedOption,
                Is.EqualTo(PauseMenuOption.Resume));
        }

        [Test]
        public void Restart_RequiresExplicitYesConfirmation()
        {
            var model = new PauseMenuModel();
            model.Open();
            model.SelectOption(PauseMenuOption.Restart);

            Assert.That(
                model.Confirm(),
                Is.EqualTo(PauseMenuAction.None));
            Assert.That(
                model.Page,
                Is.EqualTo(PauseMenuPage.Confirmation));
            Assert.That(
                model.ConfirmationTarget,
                Is.EqualTo(PauseMenuOption.Restart));
            Assert.That(model.ConfirmationYesSelected, Is.False);

            Assert.That(
                model.Confirm(),
                Is.EqualTo(PauseMenuAction.None));
            Assert.That(model.Page, Is.EqualTo(PauseMenuPage.Main));
        }

        [Test]
        public void QuitConfirmation_ReturnsQuitOnlyAfterYes()
        {
            var model = new PauseMenuModel();
            model.Open();
            model.SelectOption(PauseMenuOption.Quit);
            model.Confirm();
            model.SelectConfirmation(true);

            Assert.That(
                model.Confirm(),
                Is.EqualTo(PauseMenuAction.Quit));
        }

        [Test]
        public void Cancel_LeavesConfirmationBeforeResuming()
        {
            var model = new PauseMenuModel();
            model.Open();
            model.SelectOption(PauseMenuOption.Restart);
            model.Confirm();

            Assert.That(
                model.Cancel(),
                Is.EqualTo(PauseMenuAction.None));
            Assert.That(model.Page, Is.EqualTo(PauseMenuPage.Main));
            Assert.That(
                model.Cancel(),
                Is.EqualTo(PauseMenuAction.Resume));
        }
    }
}
