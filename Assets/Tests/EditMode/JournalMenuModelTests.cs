using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class JournalMenuModelTests
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

        [Test]
        public void EmptyLog_HasNoSelection()
        {
            var model = new JournalMenuModel();

            model.Open(new List<QuestLogEntry>());

            Assert.That(model.Count, Is.EqualTo(0));
            Assert.That(model.SelectedIndex, Is.EqualTo(-1));
            Assert.That(model.HasSelection, Is.False);
            Assert.That(model.MoveSelection(1), Is.False);
            Assert.That(
                model.TryGetSelectedDefinition(out _, out _),
                Is.False,
                "An empty log has no description to show.");
        }

        [Test]
        public void Open_PutsTheCursorOnTheFirstEntry()
        {
            var model = new JournalMenuModel();

            model.Open(GameSessionState.Quests);

            Assert.That(
                model.Count,
                Is.EqualTo(GameSessionState.Quests.Count));
            Assert.That(model.SelectedIndex, Is.EqualTo(0));
            Assert.That(
                model.TryGetSelectedDefinition(
                    out QuestDefinition definition,
                    out bool completed),
                Is.True);
            Assert.That(
                definition.Id,
                Is.EqualTo(QuestId.ReachMothersHouse));
            Assert.That(completed, Is.False);
        }

        [Test]
        public void MoveSelection_WrapsBothWays()
        {
            GameSessionState.TrySetDebugGameDay(
                GameDaySchedule.GetFirstDayNumber(
                    GameDayEventId.FeedTheCatOpens));
            var model = new JournalMenuModel();
            model.Open(GameSessionState.Quests);
            int count = model.Count;
            Assert.That(
                count,
                Is.GreaterThan(1),
                "The wrap is only meaningful with more than one entry.");

            Assert.That(model.MoveSelection(-1), Is.True);
            Assert.That(model.SelectedIndex, Is.EqualTo(count - 1));
            Assert.That(model.MoveSelection(1), Is.True);
            Assert.That(model.SelectedIndex, Is.EqualTo(0));
        }

        [Test]
        public void SelectIndex_RefusesWhatIsNotThere()
        {
            var model = new JournalMenuModel();
            model.Open(GameSessionState.Quests);

            Assert.That(model.SelectIndex(-1), Is.False);
            Assert.That(model.SelectIndex(model.Count), Is.False);
            Assert.That(
                model.SelectIndex(0),
                Is.False,
                "Selecting the row already selected changes nothing.");
            Assert.That(model.SelectedIndex, Is.EqualTo(0));
        }

        [Test]
        public void CompletedQuest_KeepsItsRowAndSwitchesDescription()
        {
            var model = new JournalMenuModel();
            GameSessionState.EnterMothersHouse();

            model.Open(GameSessionState.Quests);

            Assert.That(model.SelectedIndex, Is.EqualTo(0));
            Assert.That(
                model.SelectedEntry.Status,
                Is.EqualTo(QuestStatus.Completed));
            Assert.That(
                model.TryGetSelectedDefinition(
                    out QuestDefinition definition,
                    out bool completed),
                Is.True);
            Assert.That(completed, Is.True);
            Assert.That(
                definition.CompletedDescriptionLocalizationKey,
                Is.EqualTo("quest.mothers_house.description.completed"));
        }
    }

    public sealed class JournalViewLayoutTests
    {
        [Test]
        public void Panel_StaysInsideTheLogicalCanvas()
        {
            Rect panel = JournalView.ResolvePanelRect();

            Assert.That(panel.xMin, Is.GreaterThanOrEqualTo(0f));
            Assert.That(panel.yMin, Is.GreaterThanOrEqualTo(0f));
            Assert.That(
                panel.xMax,
                Is.LessThanOrEqualTo(RetroUiTheme.LogicalWidth));
            Assert.That(
                panel.yMax,
                Is.LessThanOrEqualTo(RetroUiTheme.LogicalHeight));
        }

        [Test]
        public void ListAndDescription_DoNotOverlap()
        {
            Rect lastRow = JournalView.ResolveRowRect(
                JournalView.VisibleRowCount - 1);
            Rect description = JournalView.ResolveDescriptionRect();
            Rect hint = JournalView.ResolveHintRect();
            Rect panel = JournalView.ResolvePanelRect();

            Assert.That(
                lastRow.yMax,
                Is.LessThanOrEqualTo(description.yMin),
                "The bottom row must not run into the description.");
            Assert.That(
                description.yMax,
                Is.LessThanOrEqualTo(hint.yMin));
            Assert.That(hint.yMax, Is.LessThanOrEqualTo(panel.yMax));
        }

        [Test]
        public void RowParts_StayInsideTheirRow()
        {
            Rect row = JournalView.ResolveRowRect(0);
            Rect box = JournalView.ResolveCheckboxRect(row);
            Rect title = JournalView.ResolveRowTitleRect(row);
            Rect status = JournalView.ResolveRowStatusRect(row);

            Assert.That(row.Contains(box.center), Is.True);
            Assert.That(box.xMax, Is.LessThanOrEqualTo(title.xMin));
            Assert.That(title.xMax, Is.LessThanOrEqualTo(status.xMin));
            Assert.That(status.xMax, Is.EqualTo(row.xMax).Within(0.01f));
            Assert.That(
                title.width,
                Is.GreaterThan(0f),
                "A title squeezed to nothing draws no quest name.");
        }

        [Test]
        public void ShortLog_NeverScrolls()
        {
            Assert.That(
                JournalView.ResolveFirstVisibleRow(
                    0,
                    JournalView.VisibleRowCount),
                Is.EqualTo(0));
            Assert.That(
                JournalView.ResolveFirstVisibleRow(
                    JournalView.VisibleRowCount - 1,
                    JournalView.VisibleRowCount),
                Is.EqualTo(0),
                "A log that fits does not move under its own selection.");
        }

        [Test]
        public void LongLog_KeepsTheSelectionOnScreen()
        {
            const int count = 20;
            for (int selected = 0; selected < count; selected++)
            {
                int first = JournalView.ResolveFirstVisibleRow(
                    selected,
                    count);

                Assert.That(first, Is.GreaterThanOrEqualTo(0));
                Assert.That(
                    first,
                    Is.LessThanOrEqualTo(
                        count - JournalView.VisibleRowCount));
                Assert.That(
                    selected,
                    Is.InRange(
                        first,
                        first + JournalView.VisibleRowCount - 1),
                    $"Row {selected} fell outside the visible window.");
            }
        }

        [Test]
        public void RowIndexAt_ReadsTheScrolledWindow()
        {
            const int count = 20;
            const int selected = 15;
            int first = JournalView.ResolveFirstVisibleRow(
                selected,
                count);

            int hit = JournalView.ResolveRowIndexAt(
                JournalView.ResolveRowRect(2).center,
                selected,
                count);

            Assert.That(
                hit,
                Is.EqualTo(first + 2),
                "The third slot holds the third row of the window.");
            Assert.That(
                JournalView.ResolveRowIndexAt(
                    new Vector2(-40f, -40f),
                    selected,
                    count),
                Is.EqualTo(-1));
        }
    }
}
