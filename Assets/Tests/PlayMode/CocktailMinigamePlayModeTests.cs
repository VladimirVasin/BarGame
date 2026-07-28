using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CocktailMinigamePlayModeTests
    {
        private const string InteriorRootName =
            "[Bar Promenade] Bar Interior Runtime";
        private const string ActiveBarId = "bar-cocktail-test";
        private const float TimeoutSeconds = 15f;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            ResetSession();
            GameSessionState.EnterBar(ActiveBarId);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            BarInteriorRoot interior =
                UnityEngine.Object.FindAnyObjectByType<BarInteriorRoot>();
            interior?.CocktailMinigame?.Cancel();
            if (interior != null)
            {
                UnityEngine.Object.Destroy(interior.gameObject);
            }

            ResetSession();
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Open_LoadsSpriteAtlasAndLocksModalInput()
        {
            BarInteriorRoot interior = null;
            yield return LoadInterior(root => interior = root);

            CocktailMinigameController minigame =
                interior.CocktailMinigame;
            Assert.That(minigame.Open(interior.Player.Interactor), Is.True);
            Assert.That(
                minigame.PresentationPhase,
                Is.EqualTo(CocktailPresentationPhase.ChoosingBase));
            Assert.That(CocktailSpriteLibrary.IsAvailable, Is.True);
            Assert.That(CocktailSpriteLibrary.Atlas.width, Is.GreaterThan(512));
            Assert.That(CocktailSpriteLibrary.Atlas.height, Is.GreaterThan(512));
            Assert.That(interior.Player.Motor.InputEnabled, Is.False);
            Assert.That(interior.Player.Interactor.InputEnabled, Is.False);
            Assert.That(
                Camera.main.GetComponent<PlayerCameraFollow>()
                    .OrbitInputEnabled,
                Is.False);

            minigame.Cancel();
            Assert.That(interior.Player.Motor.InputEnabled, Is.True);
            Assert.That(interior.Player.Interactor.InputEnabled, Is.True);
            Assert.That(
                Camera.main.GetComponent<PlayerCameraFollow>()
                    .OrbitInputEnabled,
                Is.True);
            Assert.That(
                GameSessionState.IsBarVisited(ActiveBarId),
                Is.False);

            PlayerCameraFollow follow =
                Camera.main.GetComponent<PlayerCameraFollow>();
            interior.Player.Motor.SetInputEnabled(false);
            interior.Player.Interactor.SetInputEnabled(false);
            follow.SetOrbitInputEnabled(false);
            Assert.That(
                minigame.Open(interior.Player.Interactor),
                Is.True);
            minigame.Cancel();
            Assert.That(interior.Player.Motor.InputEnabled, Is.False);
            Assert.That(interior.Player.Interactor.InputEnabled, Is.False);
            Assert.That(follow.OrbitInputEnabled, Is.False);
            interior.Player.Motor.SetInputEnabled(true);
            interior.Player.Interactor.SetInputEnabled(true);
            follow.SetOrbitInputEnabled(true);
        }

        [UnityTest]
        public IEnumerator ThreePerfectCocktails_PersistProgressAndScoreThreeHundred()
        {
            BarInteriorRoot interior = null;
            yield return LoadInterior(root => interior = root);
            CocktailMinigameController minigame =
                interior.CocktailMinigame;
            int completionCount = 0;
            minigame.Completed += () => completionCount++;
            GameSessionState.TryAddRouteStop(ActiveBarId);
            Assert.That(minigame.Open(interior.Player.Interactor), Is.True);
            Assert.That(
                GameSessionState.IsBarVisited(ActiveBarId),
                Is.False);

            for (int round = 0;
                 round < CocktailMinigameSession.RoundLimit;
                 round++)
            {
                MixPerfectBeerCocktail(minigame);
                Assert.That(
                    minigame.PresentationPhase,
                    Is.EqualTo(CocktailPresentationPhase.RoundResult));
                Assert.That(
                    minigame.LastRoundResult.Score,
                    Is.EqualTo(100));
                Assert.That(
                    GameSessionState.IsBarVisited(ActiveBarId),
                    Is.False,
                    "The visit is not complete before the result is accepted.");
                minigame.AdvancePresentation(
                    CocktailMinigameController.RoundResultDuration);
                Assert.That(
                    completionCount,
                    Is.EqualTo(
                        round ==
                        CocktailMinigameSession.RoundLimit - 1
                            ? 1
                            : 0));
                Assert.That(
                    GameSessionState.IsBarVisited(ActiveBarId),
                    Is.EqualTo(
                        round ==
                        CocktailMinigameSession.RoundLimit - 1));
            }

            Assert.That(
                minigame.PresentationPhase,
                Is.EqualTo(CocktailPresentationPhase.FinalResult));
            minigame.AdvancePresentation(
                CocktailMinigameController.RoundResultDuration);
            Assert.That(completionCount, Is.EqualTo(1));
            Assert.That(minigame.TotalScore, Is.EqualTo(300));
            Assert.That(minigame.FinalRankKey, Is.EqualTo("cocktail.rank.perfect"));
            Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(3));
            Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(24));
            Assert.That(
                GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.LightBeer));
            Assert.That(
                GameSessionState.PlannedBarRoute,
                Does.Not.Contain(ActiveBarId));
        }

        [UnityTest]
        public IEnumerator BadServedMix_PersistsPenaltyWithoutExtraStatus()
        {
            BarInteriorRoot interior = null;
            yield return LoadInterior(root => interior = root);
            CocktailMinigameController minigame =
                interior.CocktailMinigame;
            Assert.That(minigame.Open(interior.Player.Interactor), Is.True);

            Assert.That(minigame.ChooseBase(0), Is.True);
            minigame.AdvancePresentation(
                CocktailMinigameController.BasePourDuration);
            CocktailBaseId baseId = minigame.CurrentBase;
            int badIndex = FindOffer(
                minigame,
                id => !CocktailRules.AreCompatible(baseId, id));
            int goodIndex = FindOffer(
                minigame,
                id => CocktailRules.AreCompatible(baseId, id));
            Assert.That(badIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(goodIndex, Is.GreaterThanOrEqualTo(0));

            Assert.That(minigame.AddIngredient(badIndex), Is.True);
            minigame.AdvancePresentation(
                CocktailMinigameController.IngredientPourDuration);
            Assert.That(GameSessionState.IntoxicationLevel, Is.Zero);

            Assert.That(minigame.AddIngredient(goodIndex), Is.True);
            minigame.AdvancePresentation(
                CocktailMinigameController.IngredientPourDuration);
            Assert.That(minigame.CanServe, Is.True);
            Assert.That(minigame.ServeCocktail(), Is.True);
            Assert.That(
                minigame.PresentationPhase,
                Is.EqualTo(CocktailPresentationPhase.Serving));

            Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(18));
            Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(1));
            Assert.That(minigame.LastRoundResult.HasBadMix, Is.True);

            minigame.Cancel();
            Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(18));
            Assert.That(
                GameSessionState.IsBarVisited(ActiveBarId),
                Is.False);
            Assert.That(interior.Player.Motor.InputEnabled, Is.True);
            Assert.That(interior.Player.Interactor.InputEnabled, Is.True);
        }

        private static void MixPerfectBeerCocktail(
            CocktailMinigameController minigame)
        {
            Assert.That(minigame.ChooseBase(0), Is.True);
            minigame.AdvancePresentation(
                CocktailMinigameController.BasePourDuration);
            for (int addition = 0;
                 addition < CocktailMinigameSession.MaximumAdditions;
                 addition++)
            {
                CocktailBaseId baseId = minigame.CurrentBase;
                int safeIndex = FindOffer(
                    minigame,
                    id =>
                        CocktailRules.AreCompatible(baseId, id) &&
                        !minigame.IsIngredientUsed(id));
                Assert.That(
                    safeIndex,
                    Is.GreaterThanOrEqualTo(0),
                    $"Addition {addition + 1} has no safe offer.");
                Assert.That(minigame.AddIngredient(safeIndex), Is.True);
                minigame.AdvancePresentation(
                    CocktailMinigameController.IngredientPourDuration);
            }

            Assert.That(
                minigame.PresentationPhase,
                Is.EqualTo(CocktailPresentationPhase.Serving));
            minigame.AdvancePresentation(
                CocktailMinigameController.ServingDuration);
        }

        private static int FindOffer(
            CocktailMinigameController minigame,
            Predicate<CocktailIngredientId> predicate)
        {
            for (int index = 0;
                 index < minigame.OfferCount;
                 index++)
            {
                if (predicate(minigame.GetOfferId(index)))
                {
                    return index;
                }
            }

            return -1;
        }

        private static IEnumerator LoadInterior(
            Action<BarInteriorRoot> capture)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                SceneIds.BarInterior,
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);

            float deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (!operation.isDone &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(operation.isDone, Is.True);
            yield return WaitUntil(
                () =>
                {
                    Scene scene = SceneManager.GetActiveScene();
                    if (scene.name != SceneIds.BarInterior)
                    {
                        return false;
                    }

                    GameObject[] roots = scene.GetRootGameObjects();
                    for (int index = 0; index < roots.Length; index++)
                    {
                        if (roots[index].name != InteriorRootName)
                        {
                            continue;
                        }

                        BarInteriorRoot root =
                            roots[index].GetComponent<BarInteriorRoot>();
                        if (root != null && root.IsInitialized)
                        {
                            capture(root);
                            return true;
                        }
                    }

                    return false;
                },
                "Bar interior did not initialize.");
        }

        private static IEnumerator WaitUntil(
            Func<bool> predicate,
            string failureMessage)
        {
            float deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (!predicate() &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(predicate(), Is.True, failureMessage);
        }

        private static void ResetSession()
        {
            GameSessionState.SetCitySeed(
                GameSessionState.DefaultCitySeed);
            GameSessionState.ClearRoute();
            GameSessionState.ClearVisitedBars();
            GameSessionState.EnterBar(null);
            GameSessionState.CompleteCityReturn();
            GameSessionState.ResetDrinkingState();
        }
    }
}
