using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class QuestLogTests
    {
        [SetUp]
        public void SetUp()
        {
            GameSessionState.BeginNewGame();
        }

        /// <summary>
        /// Puts the clock on the day the cat's quest opens, the way the
        /// calendar does it in play. Every test below the first one is
        /// about the quest itself rather than about when it starts.
        /// </summary>
        private static void ArriveAtTheCatsDay()
        {
            Assert.That(
                GameSessionState.TrySetDebugGameDay(
                    GameDaySchedule.GetFirstDayNumber(
                        GameDayEventId.FeedTheCatOpens)),
                Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            GameSessionState.BeginNewGame();
        }

        /// <summary>
        /// The first day carries exactly one entry: the house at the
        /// top of the village lane, where a new game already stands.
        /// The cat is NOT among them. The cat used to be the first
        /// thing a new game did, which meant the descent blocker stood
        /// in the hero's own stairwell from the moment he woke — and
        /// the manual tutorial path, which walks him down and out of
        /// the street door on day one, could not actually be walked.
        /// </summary>
        [Test]
        public void NewGame_OpensOnlyTheMothersHouseQuest()
        {
            Assert.That(
                GameSessionState.GameDayNumber,
                Is.EqualTo(GameDaySchedule.FirstDayNumber));
            Assert.That(GameSessionState.Quests.Count, Is.EqualTo(1));
            Assert.That(
                GameSessionState.GetQuestStatus(
                    QuestId.ReachMothersHouse),
                Is.EqualTo(QuestStatus.Active));
            Assert.That(
                GameSessionState.GetQuestStatus(QuestId.FeedTheCat),
                Is.EqualTo(QuestStatus.NotStarted));
            Assert.That(
                GameSessionState.HasDayEventFired(
                    GameDayEventId.FeedTheCatOpens),
                Is.False);
            Assert.That(
                GameSessionState.IsInventoryItemReservedForQuest(
                    InventoryItemId.OpenStewCan),
                Is.False,
                "Nothing is held back for a quest nobody has yet.");
        }

        /// <summary>
        /// The starter quest is unread until the journal is opened,
        /// which is what the corner notice blinks about. Completing it
        /// deliberately does not make it unread again: he is standing
        /// in the thing he just finished.
        /// </summary>
        [Test]
        public void StarterQuest_IsUnreadUntilTheJournalIsOpened()
        {
            Assert.That(GameSessionState.HasUnreadQuests, Is.True);
            Assert.That(GameSessionState.HasOpenedJournal, Is.False);

            GameSessionState.MarkQuestsRead();

            Assert.That(GameSessionState.HasUnreadQuests, Is.False);
            Assert.That(GameSessionState.HasOpenedJournal, Is.True);

            GameSessionState.EnterMothersHouse();

            Assert.That(
                GameSessionState.HasUnreadQuests,
                Is.False,
                "Finishing a quest is not news the corner has to break.");
        }

        /// <summary>
        /// Stepping inside closes it, from the door or from the map,
        /// and a second visit does not reopen a one-shot entry.
        /// </summary>
        [Test]
        public void EnteringTheHouse_ClosesTheStarterQuestForGood()
        {
            GameSessionState.EnterMothersHouse();

            Assert.That(
                GameSessionState.GetQuestStatus(
                    QuestId.ReachMothersHouse),
                Is.EqualTo(QuestStatus.Completed));

            GameSessionState.EnterMothersHouse();

            Assert.That(
                GameSessionState.GetQuestStatus(
                    QuestId.ReachMothersHouse),
                Is.EqualTo(QuestStatus.Completed));
            Assert.That(GameSessionState.Quests.Count, Is.EqualTo(1));
        }

        [Test]
        public void SecondDay_OpensTheFeedTheCatQuest()
        {
            ArriveAtTheCatsDay();

            Assert.That(
                GameSessionState.GetQuestStatus(QuestId.FeedTheCat),
                Is.EqualTo(QuestStatus.Active));
            Assert.That(
                GameSessionState.Quests.Count,
                Is.EqualTo(2),
                "Day one's own quest is still up: the cat is added to " +
                "the log, not put in place of what was there.");
            Assert.That(
                GameSessionState.Quests[1].Id,
                Is.EqualTo(QuestId.FeedTheCat),
                "A later quest goes at the bottom of the log.");
            Assert.That(
                GameSessionState.HasDayEventFired(
                    GameDayEventId.FeedTheCatOpens),
                Is.True);

            QuestDefinition definition =
                QuestCatalog.Get(QuestId.FeedTheCat);
            Assert.That(
                definition.TitleLocalizationKey,
                Is.Not.Empty);
            Assert.That(
                definition.ActiveDescriptionLocalizationKey,
                Is.Not.Empty);
            Assert.That(
                definition.CompletedDescriptionLocalizationKey,
                Is.Not.Empty);
        }

        [Test]
        public void CompletingTheQuest_IsOneShotAndFinal()
        {
            ArriveAtTheCatsDay();
            Assert.That(
                GameSessionState.TryCompleteQuest(QuestId.FeedTheCat),
                Is.True);
            Assert.That(
                GameSessionState.GetQuestStatus(QuestId.FeedTheCat),
                Is.EqualTo(QuestStatus.Completed));
            Assert.That(
                GameSessionState.TryCompleteQuest(QuestId.FeedTheCat),
                Is.False,
                "A completed quest must not complete twice.");
            Assert.That(
                GameSessionState.TryActivateQuest(QuestId.FeedTheCat),
                Is.False,
                "A completed quest must not become active again.");
            Assert.That(
                GameSessionState.IsQuestActive(QuestId.FeedTheCat),
                Is.False);
        }

        [Test]
        public void OpenStewCan_IsReservedWhileTheQuestIsActive()
        {
            ArriveAtTheCatsDay();
            GameSessionState.UpdateNeeds(50, 0);
            Assert.That(
                GameSessionState.TryAddInventoryItem(
                    InventoryItemId.OpenStewCan),
                Is.True);

            Assert.That(
                GameSessionState.IsInventoryItemReservedForQuest(
                    InventoryItemId.OpenStewCan),
                Is.True);

            InventoryItemUseResult result =
                GameSessionState.TryConsumeInventoryItem(
                    InventoryItemId.OpenStewCan);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(
                result.Status,
                Is.EqualTo(InventoryItemUseStatus.ReservedForQuest));
            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.OpenStewCan),
                Is.EqualTo(1),
                "The reserved can must stay in the inventory.");
            Assert.That(GameSessionState.HungerLevel, Is.EqualTo(50));
        }

        [Test]
        public void OpenStewCan_BecomesEdibleAfterTheQuestCompletes()
        {
            ArriveAtTheCatsDay();
            GameSessionState.UpdateNeeds(50, 0);
            Assert.That(
                GameSessionState.TryAddInventoryItem(
                    InventoryItemId.OpenStewCan),
                Is.True);
            Assert.That(
                GameSessionState.TryCompleteQuest(QuestId.FeedTheCat),
                Is.True);

            Assert.That(
                GameSessionState.IsInventoryItemReservedForQuest(
                    InventoryItemId.OpenStewCan),
                Is.False);

            InventoryItemUseResult result =
                GameSessionState.TryConsumeInventoryItem(
                    InventoryItemId.OpenStewCan);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.OpenStewCan),
                Is.EqualTo(0));
            Assert.That(
                GameSessionState.HungerLevel,
                Is.LessThan(50));
        }

        [Test]
        public void ClosedStewCan_IsNeverReservedForTheQuest()
        {
            GameSessionState.UpdateNeeds(50, 0);
            Assert.That(
                GameSessionState.TryAddInventoryItem(
                    InventoryItemId.ClosedStewCan),
                Is.True);

            Assert.That(
                GameSessionState.IsInventoryItemReservedForQuest(
                    InventoryItemId.ClosedStewCan),
                Is.False);

            InventoryItemUseResult result =
                GameSessionState.TryConsumeInventoryItem(
                    InventoryItemId.ClosedStewCan);

            Assert.That(result.Succeeded, Is.True);
        }
    }
}
