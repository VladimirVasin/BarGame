using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class PauseMenuModelTests
    {
        [TestCase(GameInputContext.Gameplay, false, true, true, true, true, false)]
        [TestCase(GameInputContext.Contextual, false, true, true, true, true, false)]
        [TestCase(GameInputContext.Menu, false, true, true, true, true, false)]
        [TestCase(GameInputContext.PauseMenu, false, true, true, true, true, true)]
        [TestCase(GameInputContext.Menu, false, false, true, true, true, true)]
        [TestCase(GameInputContext.PauseMenu, false, false, true, true, true, false)]
        [TestCase(GameInputContext.Movement, false, false, false, true, false, true)]
        [TestCase(GameInputContext.Movement, false, false, false, true, true, false)]
        [TestCase(GameInputContext.Contextual, false, false, false, true, true, true)]
        [TestCase(GameInputContext.Gameplay, false, false, false, true, true, false)]
        [TestCase(GameInputContext.PauseMenu, true, true, true, true, true, false)]
        [TestCase(GameInputContext.Menu, true, false, false, false, false, false)]
        [TestCase(GameInputContext.Contextual, true, false, false, false, false, false)]
        [TestCase(GameInputContext.Movement, true, false, false, false, false, false)]
        [TestCase(GameInputContext.Gameplay, true, false, false, false, false, false)]
        public void InputOwnership_PauseMenusTransitionsAndBalanceHaveExplicitPriority(
            GameInputContext context, bool transitioning, bool pauseMenuOpen,
            bool timePaused, bool modalLocked, bool blocksMovement, bool expected)
        {
            Assert.That(GameInputPolicy.Allows(
                context, transitioning, pauseMenuOpen, timePaused,
                modalLocked, blocksMovement), Is.EqualTo(expected));
        }

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
        public void OptionsEntry_OpensPageOnFirstToggleRow()
        {
            var model = new PauseMenuModel();
            model.Open();
            model.SelectOption(PauseMenuOption.Options);

            Assert.That(
                model.Confirm(),
                Is.EqualTo(PauseMenuAction.None));
            Assert.That(
                model.Page,
                Is.EqualTo(PauseMenuPage.Options));
            Assert.That(
                model.SelectedOptionsRow,
                Is.EqualTo(PauseMenuOptionsRow.DepthOfField));
        }

        [Test]
        public void OptionsNavigation_WrapsAcrossAllRows()
        {
            var model = new PauseMenuModel();
            model.Open();
            model.SelectOption(PauseMenuOption.Options);
            model.Confirm();

            Assert.That(model.MoveSelection(-1), Is.True);
            Assert.That(
                model.SelectedOptionsRow,
                Is.EqualTo(PauseMenuOptionsRow.Back));
            Assert.That(model.MoveSelection(1), Is.True);
            Assert.That(
                model.SelectedOptionsRow,
                Is.EqualTo(PauseMenuOptionsRow.DepthOfField));
        }

        [Test]
        public void OptionsToggleRow_ReportsToggleAndStaysOnPage()
        {
            var model = new PauseMenuModel();
            model.Open();
            model.SelectOption(PauseMenuOption.Options);
            model.Confirm();
            model.SelectOptionsRow(PauseMenuOptionsRow.Dither);

            Assert.That(
                model.Confirm(),
                Is.EqualTo(PauseMenuAction.ToggleGraphicsOption));
            Assert.That(
                model.Page,
                Is.EqualTo(PauseMenuPage.Options));
            Assert.That(
                model.SelectedOptionsRow,
                Is.EqualTo(PauseMenuOptionsRow.Dither));
        }

        [Test]
        public void OptionsBackAndCancel_ReturnToMainBeforeResuming()
        {
            var model = new PauseMenuModel();
            model.Open();
            model.SelectOption(PauseMenuOption.Options);
            model.Confirm();
            model.SelectOptionsRow(PauseMenuOptionsRow.Back);

            Assert.That(
                model.Confirm(),
                Is.EqualTo(PauseMenuAction.None));
            Assert.That(model.Page, Is.EqualTo(PauseMenuPage.Main));

            model.SelectOption(PauseMenuOption.Options);
            model.Confirm();
            Assert.That(
                model.Cancel(),
                Is.EqualTo(PauseMenuAction.None));
            Assert.That(model.Page, Is.EqualTo(PauseMenuPage.Main));
            Assert.That(
                model.Cancel(),
                Is.EqualTo(PauseMenuAction.Resume));
        }

        [Test]
        public void SelectOptionsRow_RequiresOptionsPage()
        {
            var model = new PauseMenuModel();
            model.Open();

            Assert.That(
                model.SelectOptionsRow(
                    PauseMenuOptionsRow.Dither),
                Is.False);
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
