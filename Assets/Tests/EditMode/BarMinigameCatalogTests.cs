using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class BarMinigameCatalogTests
    {
        private const string FutureId = "future-test-minigame";
        private static readonly BarActivityKind FutureActivity =
            (BarActivityKind)99;

        [TearDown]
        public void TearDown()
        {
            BarMinigameCatalog.Unregister(FutureId);
            GameSessionState.EnterBar(null);
        }

        [Test]
        public void BuiltIns_HaveStableUniqueIdsAndFactories()
        {
            IReadOnlyList<BarMinigameDefinition> definitions =
                BarMinigameCatalog.Definitions;
            Assert.That(definitions.Count, Is.GreaterThanOrEqualTo(3));

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var activities = new HashSet<BarActivityKind>();
            for (int index = 0; index < definitions.Count; index++)
            {
                BarMinigameDefinition definition = definitions[index];
                Assert.That(definition.Id, Is.Not.Null.And.Not.Empty);
                Assert.That(
                    definition.LabelKey,
                    Is.Not.Null.And.Not.Empty);
                Assert.That(ids.Add(definition.Id), Is.True);
                Assert.That(
                    activities.Add(definition.Activity),
                    Is.True);
            }

            Assert.That(
                BarMinigameCatalog.TryGet(
                    BarMinigameCatalog.CocktailId,
                    out BarMinigameDefinition cocktail),
                Is.True);
            Assert.That(
                cocktail.Activity,
                Is.EqualTo(BarActivityKind.Cocktail));
            Assert.That(
                BarMinigameCatalog.TryGet(
                    BarMinigameCatalog.BeerPongId,
                    out BarMinigameDefinition beerPong),
                Is.True);
            Assert.That(
                beerPong.Activity,
                Is.EqualTo(BarActivityKind.BeerPong));
            Assert.That(
                BarMinigameCatalog.TryGet(
                    BarMinigameCatalog.SplitTheGId,
                    out BarMinigameDefinition splitTheG),
                Is.True);
            Assert.That(
                splitTheG.Activity,
                Is.EqualTo(BarActivityKind.SplitTheG));
            Assert.That(
                cocktail.SortOrder,
                Is.LessThan(beerPong.SortOrder));
            Assert.That(
                beerPong.SortOrder,
                Is.LessThan(splitTheG.SortOrder));
        }

        [TestCase(
            BarMinigameCatalog.CocktailId,
            typeof(CocktailMinigameController))]
        [TestCase(
            BarMinigameCatalog.BeerPongId,
            typeof(BeerPongMinigameController))]
        [TestCase(
            BarMinigameCatalog.SplitTheGId,
            typeof(SplitTheGMinigameController))]
        public void BuiltInFactory_CreatesExpectedController(
            string id,
            Type expectedType)
        {
            Assert.That(
                BarMinigameCatalog.TryGet(
                    id,
                    out BarMinigameDefinition definition),
                Is.True);
            GameObject host = new GameObject($"Host {id}");
            try
            {
                var context = new BarMinigameFactoryContext(
                    host,
                    null,
                    default,
                    null,
                    false);
                IBarMinigame minigame = definition.Create(context);

                Assert.That(minigame, Is.Not.Null);
                Assert.That(minigame.GetType(), Is.EqualTo(expectedType));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void RegisteredFutureGame_IsNormalizedAndVisibleWithoutMenuChanges()
        {
            var definition = new BarMinigameDefinition(
                FutureId,
                FutureActivity,
                "debug.minigame.future_test",
                "interaction.future_test",
                15,
                _ => new StubMinigame());

            Assert.That(
                BarMinigameCatalog.Register(definition),
                Is.True);
            Assert.That(
                BarMinigameCatalog.Register(definition),
                Is.False);
            Assert.That(
                BarMinigameCatalog.TryGet(
                    FutureActivity,
                    out BarMinigameDefinition registered),
                Is.True);
            Assert.That(registered, Is.SameAs(definition));
            Assert.That(
                BarMinigameCatalog.Definitions,
                Does.Contain(definition));

            GameSessionState.EnterBar(
                "future-bar",
                FutureActivity);

            Assert.That(
                GameSessionState.ActiveBarActivity,
                Is.EqualTo(FutureActivity));
        }

        private sealed class StubMinigame : IBarMinigame
        {
            public bool IsOpen { get; private set; }
            public event Action Completed;

            public bool Open(PlayerInteractor interactor)
            {
                IsOpen = interactor != null;
                return IsOpen;
            }

            public void Cancel()
            {
                IsOpen = false;
            }

            public void Complete()
            {
                Completed?.Invoke();
            }
        }
    }
}
