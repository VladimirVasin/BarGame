using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class SupermarketPurchasePersistencePlayModeTests
    {
        private const string RuntimeRootName =
            "[Bar Promenade] Supermarket Interior Runtime";
        private const float TimeoutSeconds = 15f;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameSessionState.BeginNewGame();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameSessionState.BeginNewGame();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ClosedStewPurchase_RemovesProductAndPersistsAcrossReload()
        {
            SupermarketInteriorRoot firstRoot = null;
            yield return LoadSceneAndWaitForRoot(root => firstRoot = root);
            yield return WaitUntil(
                () => firstRoot.IsInitialized,
                "Supermarket runtime root did not finish initialization.");

            Assert.That(
                TryFindProduct(
                    firstRoot,
                    InventoryItemId.ClosedStewCan,
                    out SupermarketShelfView shelf,
                    out SupermarketProductView product),
                Is.True,
                "The supermarket must create one physical closed stew can.");
            Assert.That(product.SourceId, Is.Not.Null.And.Not.Empty);
            Assert.That(product.OriginalRoot.gameObject.activeSelf, Is.True);
            Assert.That(product.SelectionCollider.enabled, Is.True);
            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.ClosedStewCan),
                Is.Zero);

            string sourceId = product.SourceId;
            int cashBefore = GameSessionState.CashBalance;
            SupermarketProductOffer offer =
                SupermarketProductCatalog.GetOffer(
                    InventoryItemId.ClosedStewCan);

            Assert.That(
                firstRoot.ShelfShop.Open(
                    shelf,
                    firstRoot.Player.Interactor),
                Is.True);
            int productIndex = IndexOf(
                firstRoot.ShelfShop.Products,
                product);
            Assert.That(productIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(
                firstRoot.ShelfShop.Select(productIndex),
                Is.True);
            Assert.That(
                firstRoot.ShelfShop.ConfirmSelection(),
                Is.True);

            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(cashBefore - offer.Price));
            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.ClosedStewCan),
                Is.EqualTo(1));
            Assert.That(
                GameSessionState.IsWorldItemCollected(sourceId),
                Is.True);
            Assert.That(
                shelf.TryGetProduct(sourceId, out _),
                Is.False);
            Assert.That(product.SelectionCollider.enabled, Is.False);
            Assert.That(product.OriginalRoot.gameObject.activeSelf, Is.False);

            SupermarketInteriorRoot reloadedRoot = null;
            yield return LoadSceneAndWaitForRoot(root => reloadedRoot = root);
            yield return WaitUntil(
                () => reloadedRoot.IsInitialized,
                "Reloaded supermarket root did not finish initialization.");

            Assert.That(
                TryFindProduct(reloadedRoot, sourceId, out _),
                Is.False,
                "A purchased source must not be rebuilt on scene re-entry.");
            Assert.That(
                GameSessionState.IsWorldItemCollected(sourceId),
                Is.True);
            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.ClosedStewCan),
                Is.EqualTo(1));
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(cashBefore - offer.Price));
        }

        private static bool TryFindProduct(
            SupermarketInteriorRoot root,
            InventoryItemId itemId,
            out SupermarketShelfView shelf,
            out SupermarketProductView product)
        {
            for (int shelfIndex = 0;
                 shelfIndex < root.World.Shelves.Count;
                 shelfIndex++)
            {
                SupermarketShelfView candidateShelf =
                    root.World.Shelves[shelfIndex];
                for (int productIndex = 0;
                     productIndex < candidateShelf.Products.Count;
                     productIndex++)
                {
                    SupermarketProductView candidate =
                        candidateShelf.Products[productIndex];
                    if (candidate.ItemId == itemId)
                    {
                        shelf = candidateShelf;
                        product = candidate;
                        return true;
                    }
                }
            }

            shelf = null;
            product = null;
            return false;
        }

        private static bool TryFindProduct(
            SupermarketInteriorRoot root,
            string sourceId,
            out SupermarketProductView product)
        {
            for (int shelfIndex = 0;
                 shelfIndex < root.World.Shelves.Count;
                 shelfIndex++)
            {
                if (root.World.Shelves[shelfIndex].TryGetProduct(
                        sourceId,
                        out product))
                {
                    return true;
                }
            }

            product = null;
            return false;
        }

        private static int IndexOf(
            System.Collections.Generic.IReadOnlyList<
                SupermarketProductView> products,
            SupermarketProductView target)
        {
            for (int index = 0; index < products.Count; index++)
            {
                if (ReferenceEquals(products[index], target))
                {
                    return index;
                }
            }

            return -1;
        }

        private static IEnumerator LoadSceneAndWaitForRoot(
            Action<SupermarketInteriorRoot> capture)
        {
            Assert.That(
                Application.CanStreamedLevelBeLoaded(
                    SceneIds.SupermarketInterior),
                Is.True,
                "SupermarketInterior must be enabled in Build Settings.");

            AsyncOperation operation = SceneManager.LoadSceneAsync(
                SceneIds.SupermarketInterior,
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);

            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!operation.isDone && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(operation.isDone, Is.True, "Scene load timed out.");
            deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                Scene scene = SceneManager.GetActiveScene();
                SupermarketInteriorRoot root =
                    UnityEngine.Object.FindAnyObjectByType<
                        SupermarketInteriorRoot>();
                if (scene.name == SceneIds.SupermarketInterior &&
                    root != null &&
                    root.gameObject.scene == scene &&
                    root.gameObject.name == RuntimeRootName)
                {
                    capture(root);
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(
                $"Scene '{SceneIds.SupermarketInterior}' did not create " +
                $"exact root '{RuntimeRootName}'.");
        }

        private static IEnumerator WaitUntil(
            Func<bool> predicate,
            string failureMessage)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!predicate() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(predicate(), Is.True, failureMessage);
        }
    }
}
