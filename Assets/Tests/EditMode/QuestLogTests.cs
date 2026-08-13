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

        [TearDown]
        public void TearDown()
        {
            GameSessionState.BeginNewGame();
        }

        [Test]
        public void NewGame_ActivatesTheFeedTheCatQuest()
        {
            Assert.That(
                GameSessionState.GetQuestStatus(QuestId.FeedTheCat),
                Is.EqualTo(QuestStatus.Active));
            Assert.That(GameSessionState.Quests.Count, Is.EqualTo(1));
            Assert.That(
                GameSessionState.Quests[0].Id,
                Is.EqualTo(QuestId.FeedTheCat));

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
