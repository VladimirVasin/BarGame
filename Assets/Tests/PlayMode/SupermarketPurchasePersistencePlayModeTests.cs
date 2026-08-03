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

        [UnityTest]
        public IEnumerator ShelfBrowser_CentersProducts_CrossesAndSkipsEmptyShelf_ThenRestoresState()
        {
            SupermarketInteriorRoot root = null;
            yield return LoadSceneAndWaitForRoot(value => root = value);
            yield return WaitUntil(
                () => root.IsInitialized,
                "Supermarket runtime root did not finish initialization.");

            Assert.That(
                root.World.TryGetShelf(
                    SupermarketInteriorLayoutPlanner.DryGoodsShelfId,
                    out SupermarketShelfView dryShelf),
                Is.True);
            Assert.That(
                root.World.TryGetShelf(
                    SupermarketInteriorLayoutPlanner.PantryShelfId,
                    out SupermarketShelfView pantryShelf),
                Is.True);
            Assert.That(
                root.World.TryGetShelf(
                    SupermarketInteriorLayoutPlanner.ColdShelfId,
                    out SupermarketShelfView coldShelf),
                Is.True);

            Vector3 previousFixedPosition = new Vector3(0.8f, 2.1f, -3.2f);
            Quaternion previousFixedRotation =
                Quaternion.Euler(11f, 23f, 2f);
            const float previousFixedFieldOfView = 61f;
            root.CameraFollow.SetFixedPose(
                previousFixedPosition,
                previousFixedRotation,
                previousFixedFieldOfView);
            bool previousMotorInput = root.Player.Motor.InputEnabled;
            bool previousInteractorInput = root.Player.Interactor.InputEnabled;
            bool previousOrbitInput = root.CameraFollow.OrbitInputEnabled;
            bool previousCinematicMotion =
                root.CameraFollow.CinematicMotionEnabled;
            bool previousHudVisibility = root.IntoxicationHud.Visible;

            Assert.That(
                root.ShelfShop.Open(dryShelf, root.Player.Interactor),
                Is.True);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.True);
            Assert.That(root.ShelfShop.ActiveShelf, Is.SameAs(dryShelf));
            Assert.That(root.Player.Motor.InputEnabled, Is.False);
            Assert.That(root.Player.Interactor.InputEnabled, Is.False);
            Assert.That(root.CameraFollow.OrbitInputEnabled, Is.False);
            Assert.That(root.CameraFollow.CinematicMotionEnabled, Is.False);
            Assert.That(root.IntoxicationHud.Visible, Is.False);
            AssertSelectedProductCentered(root, dryShelf);
            AssertNavigationArrowsFlankSelectedProduct(root);

            Assert.That(root.ShelfShop.MoveSelection(1), Is.True);
            Assert.That(root.ShelfShop.ActiveShelf, Is.SameAs(dryShelf));
            Assert.That(root.ShelfShop.SelectedIndex, Is.EqualTo(1));
            AssertSelectedProductCentered(root, dryShelf);

            Assert.That(root.ShelfShop.MoveSelection(1), Is.True);
            Assert.That(root.ShelfShop.ActiveShelf, Is.SameAs(pantryShelf));
            Assert.That(root.ShelfShop.SelectedIndex, Is.Zero);
            AssertSelectedProductCentered(root, pantryShelf);

            Assert.That(root.ShelfShop.ConfirmSelection(), Is.True);
            Assert.That(root.ShelfShop.IsOpen, Is.True);
            Assert.That(root.ShelfShop.ActiveShelf, Is.SameAs(pantryShelf));
            Assert.That(pantryShelf.Products, Has.Count.EqualTo(1));
            AssertSelectedProductCentered(root, pantryShelf);

            Assert.That(root.ShelfShop.ConfirmSelection(), Is.True);
            Assert.That(root.ShelfShop.IsOpen, Is.True);
            Assert.That(pantryShelf.Products, Is.Empty);
            Assert.That(root.ShelfShop.ActiveShelf, Is.SameAs(coldShelf));
            AssertSelectedProductCentered(root, coldShelf);

            Assert.That(root.ShelfShop.MoveSelection(-1), Is.True);
            Assert.That(root.ShelfShop.ActiveShelf, Is.SameAs(dryShelf));
            Assert.That(
                root.ShelfShop.SelectedIndex,
                Is.EqualTo(root.ShelfShop.Products.Count - 1));
            AssertSelectedProductCentered(root, dryShelf);

            Assert.That(root.ShelfShop.MoveSelection(1), Is.True);
            Assert.That(root.ShelfShop.ActiveShelf, Is.SameAs(coldShelf));
            Assert.That(root.ShelfShop.SelectedIndex, Is.Zero);
            AssertSelectedProductCentered(root, coldShelf);

            root.ShelfShop.Exit();
            Assert.That(root.ShelfShop.IsOpen, Is.False);
            Assert.That(root.ShelfShop.ActiveShelf, Is.Null);
            Assert.That(root.ShelfShop.Products, Is.Empty);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
            Assert.That(root.CameraFollow.FixedPoseActive, Is.True);
            Assert.That(
                Vector3.Distance(
                    root.CameraFollow.FixedBasePosition,
                    previousFixedPosition),
                Is.LessThan(0.001f));
            Assert.That(
                Quaternion.Angle(
                    root.CameraFollow.FixedBaseRotation,
                    previousFixedRotation),
                Is.LessThan(0.01f));
            Assert.That(
                root.CameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(previousFixedFieldOfView).Within(0.001f));
            Assert.That(
                root.Player.Motor.InputEnabled,
                Is.EqualTo(previousMotorInput));
            Assert.That(
                root.Player.Interactor.InputEnabled,
                Is.EqualTo(previousInteractorInput));
            Assert.That(
                root.CameraFollow.OrbitInputEnabled,
                Is.EqualTo(previousOrbitInput));
            Assert.That(
                root.CameraFollow.CinematicMotionEnabled,
                Is.EqualTo(previousCinematicMotion));
            Assert.That(
                root.IntoxicationHud.Visible,
                Is.EqualTo(previousHudVisibility));
        }

        private static void AssertSelectedProductCentered(
            SupermarketInteriorRoot root,
            SupermarketShelfView expectedShelf)
        {
            SupermarketProductView product = root.ShelfShop.SelectedProduct;
            Assert.That(product, Is.Not.Null);
            Assert.That(product.Shelf, Is.SameAs(expectedShelf));
            Assert.That(product.TryGetWorldBounds(out Bounds bounds), Is.True);

            Camera camera = root.CameraFollow.GetComponent<Camera>();
            Vector3 viewport = camera.WorldToViewportPoint(bounds.center);
            Assert.That(viewport.z, Is.GreaterThan(0f));
            Assert.That(viewport.x, Is.EqualTo(0.5f).Within(0.002f));
            Assert.That(viewport.y, Is.EqualTo(0.5f).Within(0.002f));
            Assert.That(
                Vector3.Distance(
                    root.CameraFollow.FixedBasePosition,
                    expectedShelf.CameraPosition),
                Is.LessThan(0.001f));
            Assert.That(
                root.CameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(expectedShelf.CameraFieldOfView).Within(0.001f));

            Quaternion expectedRotation = Quaternion.LookRotation(
                bounds.center - expectedShelf.CameraPosition,
                Vector3.up);
            Assert.That(
                Quaternion.Angle(
                    root.CameraFollow.FixedBaseRotation,
                    expectedRotation),
                Is.LessThan(0.05f));
        }

        private static void AssertNavigationArrowsFlankSelectedProduct(
            SupermarketInteriorRoot root)
        {
            Assert.That(
                root.ShelfShopView.TryGetNavigationRects(
                    out Rect left,
                    out Rect right),
                Is.True);

            Camera camera = root.CameraFollow.GetComponent<Camera>();
            Assert.That(
                root.ShelfShop.SelectedProduct.TryGetWorldBounds(
                    out Bounds bounds),
                Is.True);
            Vector3 screen = camera.WorldToScreenPoint(bounds.center);
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Vector2 logicalCenter = canvas.ScreenToLogical(
                new Vector2(screen.x, Screen.height - screen.y));
            Assert.That(left.center.x, Is.LessThan(logicalCenter.x));
            Assert.That(right.center.x, Is.GreaterThan(logicalCenter.x));
            Assert.That(
                Mathf.Abs(left.center.y - logicalCenter.y),
                Is.LessThan(18f));
            Assert.That(
                Mathf.Abs(right.center.y - logicalCenter.y),
                Is.LessThan(18f));

            Vector2 leftGuiScreen = canvas.LogicalToScreen(left.center);
            Vector2 leftInputScreen = new Vector2(
                leftGuiScreen.x,
                Screen.height - leftGuiScreen.y);
            Assert.That(
                root.ShelfShopView.ContainsPointerBlockingWorldSelection(
                    leftInputScreen),
                Is.True);
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
