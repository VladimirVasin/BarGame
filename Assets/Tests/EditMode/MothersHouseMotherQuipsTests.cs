using System.Collections.Generic;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class MothersHouseMotherQuipsTests
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

        /// <summary>
        /// Ten ordinary lines plus the re-ask. The re-ask is the last
        /// entry and the only one that can retire, because it is the
        /// only one that stops being true.
        /// </summary>
        [Test]
        public void ThePoolIsTenLinesAndOneReAsk()
        {
            Assert.That(
                MothersHouseMotherQuips.LineKeys.Length,
                Is.EqualTo(11));
            Assert.That(
                MothersHouseMotherQuips.ReAskIndex,
                Is.EqualTo(10));
            Assert.That(
                MothersHouseMotherQuips.ResolvePoolSize(false),
                Is.EqualTo(11));
            Assert.That(
                MothersHouseMotherQuips.ResolvePoolSize(true),
                Is.EqualTo(10));
            CollectionAssert.AllItemsAreUnique(
                MothersHouseMotherQuips.LineKeys);
            CollectionAssert.DoesNotContain(
                MothersHouseMotherQuips.LineKeys,
                MothersHouseMotherQuips.GreetingLineKey,
                "The greeting is not in the bag: it fires on entry.");
            CollectionAssert.DoesNotContain(
                MothersHouseMotherQuips.LineKeys,
                MothersHouseMotherQuips.RequestLineKey,
                "The first request is said once, outside the bag.");
        }

        [Test]
        public void EveryLineIsLiveExceptTheRetiredReAsk()
        {
            for (int index = 0;
                 index < MothersHouseMotherQuips.LineKeys.Length;
                 index++)
            {
                Assert.That(
                    MothersHouseMotherQuips.IsLineLive(index, false),
                    Is.True);
                Assert.That(
                    MothersHouseMotherQuips.IsLineLive(index, true),
                    Is.EqualTo(
                        index != MothersHouseMotherQuips.ReAskIndex));
            }
        }

        /// <summary>
        /// The bag: every line comes out once before any comes out
        /// twice. That is the whole reason for a bag rather than a
        /// roll — ten lines rolled at random repeat inside four draws
        /// often enough to notice.
        /// </summary>
        [Test]
        public void TheBagSpendsEveryLineBeforeRepeatingOne()
        {
            var state = new MothersHouseMotherSpeechState(982451653u);
            var seen = new HashSet<int>();

            for (int draw = 0;
                 draw < MothersHouseMotherQuips.LineKeys.Length;
                 draw++)
            {
                int line = state.TakeSpokenLine(false);
                Assert.That(line, Is.InRange(0, 10));
                Assert.That(
                    seen.Add(line),
                    Is.True,
                    $"Line {line} came round again on draw {draw}.");
            }
        }

        [Test]
        public void ARefilledBagDoesNotOpenOnTheLineItClosedWith()
        {
            var state = new MothersHouseMotherSpeechState(4711u);
            int last = -1;
            for (int draw = 0;
                 draw < MothersHouseMotherQuips.LineKeys.Length;
                 draw++)
            {
                last = state.TakeSpokenLine(false);
            }

            Assert.That(
                state.TakeSpokenLine(false),
                Is.Not.EqualTo(last),
                "The seam between two bags is the one place she " +
                "could repeat herself.");
        }

        [Test]
        public void AWornScarfTakesTheReAskOutOfCirculation()
        {
            var state = new MothersHouseMotherSpeechState(90210u);

            for (int draw = 0; draw < 200; draw++)
            {
                Assert.That(
                    state.TakeSpokenLine(true),
                    Is.Not.EqualTo(MothersHouseMotherQuips.ReAskIndex),
                    "She cannot ask for a scarf he is wearing.");
            }
        }

        /// <summary>
        /// Silence is spent by visible time only, and a line on screen
        /// holds it: typing and reading must not eat the next one.
        /// </summary>
        [Test]
        public void ALineOnScreenHoldsTheSilence()
        {
            var state = new MothersHouseMotherSpeechState(12345u);
            Assert.That(
                state.SilenceRemaining,
                Is.InRange(
                    MothersHouseMotherSpeechState.FirstMinimumSeconds,
                    MothersHouseMotherSpeechState.FirstMaximumSeconds));

            Assert.That(
                state.AdvanceSilence(
                    state.SilenceRemaining - 0.1f,
                    false),
                Is.EqualTo(-1));
            Assert.That(state.SilenceRemaining, Is.EqualTo(0.1f).Within(0.001f));

            int line = state.AdvanceSilence(0.2f, false);
            Assert.That(line, Is.InRange(0, 10));
            Assert.That(state.IsLineActive, Is.True);
            Assert.That(
                state.AdvanceSilence(500f, false),
                Is.EqualTo(-1),
                "Typing and reading must not consume the next silence.");

            state.FinishLine();
            Assert.That(
                state.SilenceRemaining,
                Is.InRange(
                    MothersHouseMotherSpeechState.SilenceMinimumSeconds,
                    MothersHouseMotherSpeechState.SilenceMaximumSeconds));
        }

        [Test]
        public void APausedFrameOwesHerNothing()
        {
            var state = new MothersHouseMotherSpeechState(777u);
            float before = state.SilenceRemaining;

            Assert.That(state.AdvanceSilence(0f, false), Is.EqualTo(-1));
            Assert.That(
                state.AdvanceSilence(float.NaN, false),
                Is.EqualTo(-1));
            Assert.That(
                state.AdvanceSilence(
                    float.PositiveInfinity,
                    false),
                Is.EqualTo(-1));

            Assert.That(state.SilenceRemaining, Is.EqualTo(before));
        }

        [Test]
        public void FinishingALineTwiceDoesNotRerollTheSilence()
        {
            var state = new MothersHouseMotherSpeechState(31337u);
            state.AdvanceSilence(1000f, false);
            state.FinishLine();
            float rolled = state.SilenceRemaining;

            state.FinishLine();

            Assert.That(state.SilenceRemaining, Is.EqualTo(rolled));
        }

        /// <summary>
        /// There is no per-visit quota. A ride ends by itself and five
        /// lines fill it; a room does not, and a mother who fell silent
        /// for good would read as having nothing left to say.
        /// </summary>
        [Test]
        public void SheNeverRunsOutOfThingsToSay()
        {
            var state = new MothersHouseMotherSpeechState(2024u);

            for (int line = 0; line < 60; line++)
            {
                Assert.That(
                    state.AdvanceSilence(1000f, false),
                    Is.InRange(0, 10),
                    $"She went silent after {line} lines.");
                state.FinishLine();
            }
        }

        /// <summary>
        /// The whole chain, without a scene: arriving closes the first
        /// quest, her request opens the second, and wearing the scarf
        /// closes it. Picking the scarf up is deliberately not enough.
        /// </summary>
        [Test]
        public void TheScarfQuestRunsFromHerRequestToWearingIt()
        {
            Assert.That(
                GameSessionState.GetQuestStatus(QuestId.FindTheScarf),
                Is.EqualTo(QuestStatus.NotStarted),
                "Nobody has asked him for anything yet.");

            GameSessionState.EnterMothersHouse();
            Assert.That(
                GameSessionState.TryActivateQuest(QuestId.FindTheScarf),
                Is.True,
                "Her request is what puts the entry up.");

            Assert.That(
                GameSessionState.TryCollectWorldItem(
                    MothersHouseScarfPickupPlan.SourceId,
                    InventoryItemId.Scarf),
                Is.True);
            Assert.That(
                GameSessionState.GetQuestStatus(QuestId.FindTheScarf),
                Is.EqualTo(QuestStatus.Active),
                "Carrying it is not wearing it.");

            Assert.That(
                GameSessionState.TrySetInventoryItemEquipped(
                    InventoryItemId.Scarf,
                    true),
                Is.True);
            Assert.That(
                GameSessionState.GetQuestStatus(QuestId.FindTheScarf),
                Is.EqualTo(QuestStatus.Completed));
        }

        [Test]
        public void TakingTheScarfOffAgain_LeavesTheQuestClosed()
        {
            GameSessionState.TryActivateQuest(QuestId.FindTheScarf);
            GameSessionState.TryCollectWorldItem(
                MothersHouseScarfPickupPlan.SourceId,
                InventoryItemId.Scarf);
            GameSessionState.TrySetInventoryItemEquipped(
                InventoryItemId.Scarf,
                true);

            GameSessionState.TrySetInventoryItemEquipped(
                InventoryItemId.Scarf,
                false);

            Assert.That(
                GameSessionState.GetQuestStatus(QuestId.FindTheScarf),
                Is.EqualTo(QuestStatus.Completed));
        }

        [Test]
        public void WearingItBeforeSheAsks_DoesNotOpenOrCloseAnything()
        {
            GameSessionState.TryCollectWorldItem(
                MothersHouseScarfPickupPlan.SourceId,
                InventoryItemId.Scarf);

            GameSessionState.TrySetInventoryItemEquipped(
                InventoryItemId.Scarf,
                true);

            Assert.That(
                GameSessionState.GetQuestStatus(QuestId.FindTheScarf),
                Is.EqualTo(QuestStatus.NotStarted));
        }

        /// <summary>
        /// A new game gives her a fresh bag. Otherwise the second run
        /// would open on whatever the first one had left.
        /// </summary>
        [Test]
        public void ANewGameHandsHerAFreshSilence()
        {
            MothersHouseMotherSpeechState before =
                MothersHouseMotherSpeechSession.State;

            GameSessionState.BeginNewGame();

            Assert.That(
                MothersHouseMotherSpeechSession.State,
                Is.Not.SameAs(before));
            Assert.That(
                MothersHouseMotherSpeechSession.State.SilenceRemaining,
                Is.InRange(
                    MothersHouseMotherSpeechState.FirstMinimumSeconds,
                    MothersHouseMotherSpeechState.FirstMaximumSeconds));
        }
    }
}
