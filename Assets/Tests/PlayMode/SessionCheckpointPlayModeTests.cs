using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    [PrebuildSetup(typeof(CompactBarAssetsSetup))]
    public sealed class SessionCheckpointPlayModeTests
    {
        private const float TimeoutSeconds = 30f;
        private const string EntrySource = "checkpoint.test.before-entry";
        private const string LaterSource = "checkpoint.test.after-entry";
        private float previousCaptureDelta;
        private bool previousAudioPause;

        [SetUp]
        public void SetUp()
        {
            previousCaptureDelta = Time.captureDeltaTime;
            previousAudioPause = AudioListener.pause;
            Time.captureDeltaTime = 1f / 60f;
            GameSessionState.BeginNewGame("checkpoint-play-test");
            Assert.That(GameSessionState.TryPrepareGameTimeAt(12 * 60), Is.True);
            Assert.That(GameSessionState.TryCollectWorldItem(
                EntrySource, InventoryItemId.ClosedStewCan), Is.True);
            GameSessionState.EnterBar("bar-checkpoint-test", BarActivityKind.Cocktail,
                CityDistrictKind.Residential);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return AreaCaptureFixture.ReleaseBarService();
            Scene bar = SceneManager.GetSceneByName(SceneIds.BarInterior);
            if (bar.IsValid() && bar.isLoaded)
            {
                Scene cleanup = SceneManager.CreateScene("Checkpoint Test Cleanup");
                SceneManager.SetActiveScene(cleanup);
                yield return SceneManager.UnloadSceneAsync(bar);
            }

            GameSessionState.BeginNewGame("checkpoint-play-cleanup");
            Time.captureDeltaTime = previousCaptureDelta;
            AudioListener.pause = previousAudioPause;
            yield return null;
        }

        [UnityTest]
        public IEnumerator CheckpointMenu_ReloadsEntryProgressAndRejectsOldTokensOnRepeatedRestore()
        {
            Assert.That(SessionCheckpointService.HasCheckpoint, Is.False);
            Assert.That(SceneTransitionService.RequestCoveredLoad(SceneIds.BarInterior), Is.True);
            yield return WaitForTransition();

            BarInteriorRoot bar = FindReadyBar();
            Assert.That(SessionCheckpointService.HasCheckpoint, Is.True);
            int entryCash = GameSessionState.CashBalance;
            int entryIntoxication = GameSessionState.IntoxicationLevel;
            int entryCanCount = GameSessionState.GetInventoryItemCount(InventoryItemId.ClosedStewCan);
            double entryTime = GameSessionState.GameTimeOfDayMinutes;

            for (int repeat = 0; repeat < 2; repeat++)
            {
                bar.ArrivalPresentation?.Skip();
                Assert.That(GameSessionState.TryPurchaseDrink(DrinkId.Vodka).Succeeded, Is.True);
                Assert.That(GameSessionState.TryCollectWorldItem(
                    LaterSource, InventoryItemId.ClosedStewCan), Is.True);
                Assert.That(GameSessionState.TryStartPreparedGameTime(), Is.True);
                GameSessionState.AdvanceGameTime(120f);
                Assert.That(GameSessionState.TryOrderDrink(DrinkId.Vodka,
                    out DrinkOrderToken oldOrder).Succeeded, Is.True);
                Assert.That(GameSessionState.CashBalance, Is.LessThan(entryCash));
                Assert.That(GameSessionState.IntoxicationLevel, Is.GreaterThan(entryIntoxication));
                Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.GreaterThan(entryTime));

                BarInteriorRoot oldBar = bar;
                GameObject oldPlayer = bar.Player.GameObject;
                PauseMenuController menu = bar.PauseMenu;
                Assert.That(menu.Open(), Is.True);
                Assert.That(Time.timeScale, Is.Zero);
                Assert.That(AudioListener.pause, Is.True);
                Assert.That(menu.MoveSelection(1), Is.True);
                Assert.That(menu.SelectedOption, Is.EqualTo(PauseMenuOption.ReturnToCheckpoint));
                Assert.That(menu.ConfirmSelection(), Is.True);
                Assert.That(menu.Page, Is.EqualTo(PauseMenuPage.Confirmation));
                Assert.That(menu.ConfirmationYesSelected, Is.False);
                Assert.That(menu.MoveSelection(1), Is.True);
                Assert.That(menu.ConfirmationYesSelected, Is.True);
                Assert.That(menu.ConfirmSelection(), Is.True);
                Assert.That(SessionCheckpointService.IsRestoring, Is.True);

                yield return WaitForTransition();
                bar = FindReadyBar();
                Assert.That(oldBar == null, Is.True, "The old world must finish cleanup before restore.");
                Assert.That(oldPlayer == null, Is.True, "The restored scene needs a fresh player.");
                Assert.That(GameSessionState.CashBalance, Is.EqualTo(entryCash));
                Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(entryIntoxication));
                Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.EqualTo(entryTime).Within(.00001d));
                Assert.That(GameSessionState.IsGameTimeRunning, Is.False);
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.ClosedStewCan),
                    Is.EqualTo(entryCanCount));
                Assert.That(GameSessionState.IsWorldItemCollected(EntrySource), Is.True);
                Assert.That(GameSessionState.IsWorldItemCollected(LaterSource), Is.False);
                Assert.That(GameSessionState.TryConsumeOrderedDrink(oldOrder), Is.False,
                    "A paid order from the abandoned timeline must not change restored progress.");
                Assert.That(GameSessionState.ActiveBarId, Is.EqualTo("bar-checkpoint-test"));
                Assert.That(SessionCheckpointService.HasCheckpoint, Is.True);
                Assert.That(PauseMenuController.IsAnyPaused, Is.False);
                Assert.That(AudioListener.pause, Is.False);
                Assert.That(Time.timeScale, Is.GreaterThan(0f));
                Assert.That(SceneManager.GetSceneByName(SceneIds.City).isLoaded, Is.False);
            }
        }

        private static IEnumerator WaitForTransition()
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while ((SceneTransitionService.IsTransitioning || SessionCheckpointService.IsRestoring) &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;

            // The transition's final nested iterator and deferred destruction
            // release their pause/camera owners at the end of this frame.
            yield return null;

            Assert.That(SceneTransitionService.IsTransitioning, Is.False,
                $"Transition did not finish: {SceneTransitionService.CurrentOperationId}.");
            Assert.That(SessionCheckpointService.IsRestoring, Is.False,
                "Checkpoint restore did not release its operation.");
        }

        private static BarInteriorRoot FindReadyBar()
        {
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneIds.BarInterior));
            BarInteriorRoot bar = Object.FindAnyObjectByType<BarInteriorRoot>();
            Assert.That(bar, Is.Not.Null);
            Assert.That(bar.IsInitialized, Is.True);
            Assert.That(bar.PauseMenu, Is.Not.Null);
            return bar;
        }
    }
}
