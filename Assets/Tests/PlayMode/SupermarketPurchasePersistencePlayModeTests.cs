using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
            SupermarketInteriorRoot root = UnityEngine.Object.FindAnyObjectByType<SupermarketInteriorRoot>();
            if (root != null) root.ShelfShop?.Close();
            GameSessionState.BeginNewGame();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Scene_BootstrapsOptionalMusicThroughSharedMixer()
        {
            SupermarketInteriorRoot root = null;
            yield return LoadSceneAndWaitForRoot(value => root = value);
            yield return WaitUntil(
                () => root.IsInitialized,
                "Supermarket runtime root did not finish initialization.");

            Assert.That(root.Music, Is.Not.Null);
            Assert.That(
                root.Music.transform.IsChildOf(root.transform),
                Is.True);
            Assert.That(
                SupermarketMusicPlayer.ResourcePath,
                Is.EqualTo(
                    "Audio/SupermarketMusic/supermarket_theme"));
            Assert.That(root.Music.Source, Is.Not.Null);
            Assert.That(root.Music.Source.loop, Is.True);
            Assert.That(root.Music.Source.playOnAwake, Is.False);
            Assert.That(root.Music.Source.spatialBlend, Is.Zero);
            Assert.That(root.Music.ToneFilter, Is.Not.Null);
            Assert.That(
                root.Music.Source.outputAudioMixerGroup,
                Is.SameAs(GameAudioMixer.MusicGroup));
            Assert.That(
                GameAudioMixer.CurrentProfile,
                Is.EqualTo(GameAudioProfile.Home));

            SupermarketMusicPlayer[] players =
                UnityEngine.Object.FindObjectsByType<
                    SupermarketMusicPlayer>(
                    FindObjectsInactive.Exclude);
            Assert.That(players, Has.Length.EqualTo(1));

            AudioClip expectedClip = Resources.Load<AudioClip>(
                SupermarketMusicPlayer.ResourcePath);
            Assert.That(root.Music.ActiveClip, Is.SameAs(expectedClip));
            if (expectedClip == null)
            {
                Assert.That(
                    root.Music.PlaybackState,
                    Is.EqualTo(SceneMusicPlaybackState.Unavailable));
            }
            else
            {
                Assert.That(
                    expectedClip.name,
                    Is.EqualTo(SupermarketMusicPlayer.TrackName));
            }
        }

        [UnityTest]
        public IEnumerator Purchases_KeepStockAndReceiptOwnershipAcrossRepeatReloadAndFailures()
        {
            SupermarketInteriorRoot root = null;
            yield return LoadSceneAndWaitForRoot(value => root = value);
            yield return WaitUntil(() => root.IsInitialized,
                "Supermarket runtime root did not finish initialization.");
            WorldItemFoundScreen receipt = WorldItemFoundScreen.For(root.Player.Interactor);
            Assert.That(receipt != null && receipt.IsInitialized, Is.True);
            int cashBefore = GameSessionState.CashBalance;
            int spent = 0;
            var sourceIds = new List<string>();

            for (int offerIndex = 0; offerIndex < SupermarketProductCatalog.Offers.Count; offerIndex++)
            {
                SupermarketProductOffer offer = SupermarketProductCatalog.Offers[offerIndex];
                Assert.That(TryFindProduct(root, offer.ItemId,
                    out SupermarketShelfView shelf, out SupermarketProductView product), Is.True);
                sourceIds.Add(product.SourceId);
                Assert.That(root.ShelfShop.Open(shelf, root.Player.Interactor), Is.True);
                Assert.That(root.ShelfShop.Select(IndexOf(root.ShelfShop.Products, product)), Is.True);
                yield return null;
                yield return null;
                if (offerIndex == 0)
                {
                    receipt.enabled = false;
                    Assert.That(root.ShelfShop.ConfirmSelection(), Is.False,
                        "A failed receipt preparation must not spend money or grant stock.");
                    Assert.That(GameSessionState.CashBalance, Is.EqualTo(cashBefore));
                    Assert.That(GameSessionState.GetInventoryItemCount(offer.ItemId), Is.Zero);
                    Assert.That(root.ShelfShop.IsReceiptOpen, Is.False);
                    receipt.enabled = true;
                    yield return null;
                    yield return null;
                }
                for (int repeat = 1; repeat <= 2; repeat++)
                {
                    Assert.That(root.ShelfShop.ConfirmSelection(), Is.True);
                    spent += offer.Price;
                    Assert.That(root.ShelfShop.IsReceiptOpen, Is.True);
                    Assert.That(receipt.ActionKey, Is.EqualTo(WorldItemFoundScreen.CloseActionKey));
                    Assert.That(receipt.FeedbackKey, Is.EqualTo(SupermarketShelfShopController.ReceivedFeedbackKey));
                    Assert.That(root.ShelfShop.ConfirmSelection(), Is.False,
                        "A repeated confirm during the same receipt must not buy another item.");
                    Assert.That(root.ShelfShop.MoveSelection(1), Is.False);
                    Assert.That(root.ShelfShop.Select(0), Is.False);
                    Assert.That(GameSessionState.CashBalance, Is.EqualTo(cashBefore - spent));
                    Assert.That(GameSessionState.GetInventoryItemCount(offer.ItemId), Is.EqualTo(repeat));
                    Assert.That(GameSessionState.IsWorldItemCollected(product.SourceId), Is.False);
                    Assert.That(shelf.TryGetProduct(product.SourceId, out _), Is.True);
                    Assert.That(product.OriginalRoot.gameObject.activeSelf, Is.True);
                    Assert.That(product.SelectionCollider.enabled, Is.True);
                    Assert.That(BarMinigameModalLock.IsAnyLocked, Is.True);
                    Assert.That(root.Player.Motor.InputEnabled, Is.False);
                    Assert.That(root.Player.Interactor.InputEnabled, Is.False);

                    yield return WaitUntil(() => receipt.IsShowing, "The shared shop receipt did not settle.");
                    if (offer.ItemId == InventoryItemId.VodkaBottle && repeat == 1)
                        yield return CaptureReceipt(root.CameraFollow.Camera);
                    if (repeat == 1) Assert.That(receipt.Dismiss(), Is.True);
                    else Assert.That(receipt.Abandon(), Is.True);
                    if (repeat == 1)
                        yield return WaitUntil(() => !root.ShelfShop.IsReceiptOpen, "The receipt did not close.");
                    Assert.That(root.ShelfShop.ConfirmSelection(), Is.False,
                        "The receipt closing frame cannot start the next purchase.");
                    Assert.That(root.ShelfShop.SelectedProduct, Is.SameAs(product));
                    Assert.That(root.ShelfShop.IsOpen && BarMinigameModalLock.IsAnyLocked, Is.True,
                        "A nested receipt must retain the shelf browser's modal ownership.");
                    Assert.That(root.Player.Motor.InputEnabled || root.Player.Interactor.InputEnabled, Is.False);
                    yield return null;
                    yield return null;
                }
                root.ShelfShop.Close();
                Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
                Assert.That(root.Player.Motor.InputEnabled && root.Player.Interactor.InputEnabled, Is.True);
            }

            // An old sold-source bit must not remove the replenishable offer on re-entry.
            string legacySource = sourceIds[sourceIds.Count - 1];
            InventoryItemId legacyItem = SupermarketProductCatalog.Offers[sourceIds.Count - 1].ItemId;
            Assert.That(GameSessionState.TryCollectWorldItem(legacySource, legacyItem), Is.True);
            yield return LoadSceneAndWaitForRoot(value => root = value);
            yield return WaitUntil(() => root.IsInitialized, "Reloaded supermarket did not initialize.");
            foreach (string source in sourceIds)
                Assert.That(TryFindProduct(root, source, out _), Is.True);
            Assert.That(GameSessionState.CashBalance, Is.EqualTo(cashBefore - spent));
            foreach (SupermarketProductOffer offer in SupermarketProductCatalog.Offers)
                Assert.That(GameSessionState.GetInventoryItemCount(offer.ItemId),
                    Is.EqualTo(offer.ItemId == legacyItem ? 3 : 2));

            Assert.That(TryFindProduct(root, InventoryItemId.ClosedStewCan,
                out SupermarketShelfView fullShelf, out SupermarketProductView fullProduct), Is.True);
            Assert.That(GameSessionState.TryAddInventoryItem(InventoryItemId.ClosedStewCan,
                9 - GameSessionState.GetInventoryItemCount(InventoryItemId.ClosedStewCan)), Is.True);
            Assert.That(root.ShelfShop.Open(fullShelf, root.Player.Interactor), Is.True);
            Assert.That(root.ShelfShop.Select(IndexOf(root.ShelfShop.Products, fullProduct)), Is.True);
            yield return null;
            yield return null;
            Assert.That(root.ShelfShop.ConfirmSelection(), Is.False);
            Assert.That(root.ShelfShop.FeedbackKey, Is.EqualTo("supermarket.failure.inventory_full"));
            Assert.That(root.ShelfShop.IsReceiptOpen, Is.False);
            Assert.That(GameSessionState.CashBalance, Is.EqualTo(cashBefore - spent));
            Assert.That(fullProduct.OriginalRoot.gameObject.activeSelf && fullProduct.SelectionCollider.enabled, Is.True);
            root.ShelfShop.Close();

            // Exercise the actual cash commit boundary without manufacturing more world sources.
            while (GameSessionState.CashBalance >= 28)
            {
                Assert.That(GameSessionState.TryRemoveInventoryItem(InventoryItemId.VodkaBottle,
                    GameSessionState.GetInventoryItemCount(InventoryItemId.VodkaBottle)), Is.True);
                Assert.That(GameSessionState.TryPurchaseWorldItem("supermarket.test.cash", InventoryItemId.VodkaBottle).Succeeded,
                    Is.True);
            }
            Assert.That(TryFindProduct(root, InventoryItemId.VodkaBottle,
                out SupermarketShelfView vodkaShelf, out SupermarketProductView vodkaProduct), Is.True);
            int cashAtRefusal = GameSessionState.CashBalance;
            int bottlesAtRefusal = GameSessionState.GetInventoryItemCount(InventoryItemId.VodkaBottle);
            Assert.That(root.ShelfShop.Open(vodkaShelf, root.Player.Interactor), Is.True);
            Assert.That(root.ShelfShop.Select(IndexOf(root.ShelfShop.Products, vodkaProduct)), Is.True);
            yield return null;
            yield return null;
            Assert.That(root.ShelfShop.ConfirmSelection(), Is.False);
            Assert.That(root.ShelfShop.FeedbackKey, Is.EqualTo("supermarket.failure.insufficient_funds"));
            Assert.That(root.ShelfShop.IsReceiptOpen, Is.False);
            Assert.That(GameSessionState.CashBalance, Is.EqualTo(cashAtRefusal));
            Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.VodkaBottle), Is.EqualTo(bottlesAtRefusal));
            root.ShelfShop.Close();
        }

        [UnityTest]
        public IEnumerator ShelfBrowser_CentersProducts_CrossesStockedShelves_ThenRestoresState()
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
            IReadOnlyList<Renderer> playerRenderers =
                root.Player.Visual.Renderers;
            IReadOnlyList<Renderer> cashierRenderers =
                root.Cashier.Presentation.Renderers;
            bool[] previousPlayerRendererStates =
                CaptureRendererStates(playerRenderers);
            bool[] previousCashierRendererStates =
                CaptureRendererStates(cashierRenderers);
            Assert.That(playerRenderers, Is.Not.Empty);
            Assert.That(cashierRenderers, Is.Not.Empty);

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
            Assert.That(
                root.Player.PresentationVisibility.RenderersHidden,
                Is.True);
            AssertAllRenderersDisabled(playerRenderers);
            AssertAllRenderersDisabled(cashierRenderers);
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

            Assert.That(root.ShelfShop.MoveSelection(1), Is.True);
            Assert.That(root.ShelfShop.ActiveShelf, Is.SameAs(pantryShelf));
            Assert.That(root.ShelfShop.SelectedIndex, Is.EqualTo(1));
            AssertSelectedProductCentered(root, pantryShelf);

            Assert.That(root.ShelfShop.MoveSelection(1), Is.True);
            Assert.That(root.ShelfShop.ActiveShelf, Is.SameAs(coldShelf));
            AssertSelectedProductCentered(root, coldShelf);

            Assert.That(root.ShelfShop.MoveSelection(-1), Is.True);
            Assert.That(root.ShelfShop.ActiveShelf, Is.SameAs(pantryShelf));
            Assert.That(root.ShelfShop.SelectedIndex, Is.EqualTo(1));
            AssertSelectedProductCentered(root, pantryShelf);

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
            Assert.That(
                root.Player.PresentationVisibility.RenderersHidden,
                Is.False);
            AssertRendererStates(
                playerRenderers,
                previousPlayerRendererStates);
            AssertRendererStates(
                cashierRenderers,
                previousCashierRendererStates);
        }

        [UnityTest]
        public IEnumerator ShelfBrowser_DisableRestoresCharacterRenderers()
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

            IReadOnlyList<Renderer> playerRenderers =
                root.Player.Visual.Renderers;
            IReadOnlyList<Renderer> cashierRenderers =
                root.Cashier.Presentation.Renderers;
            bool[] playerStates = CaptureRendererStates(playerRenderers);
            bool[] cashierStates = CaptureRendererStates(cashierRenderers);

            Assert.That(
                root.ShelfShop.Open(dryShelf, root.Player.Interactor),
                Is.True);
            yield return null;
            yield return null;

            AssertAllRenderersDisabled(playerRenderers);
            AssertAllRenderersDisabled(cashierRenderers);
            InventoryItemId boughtItem = root.ShelfShop.SelectedProduct.ItemId;
            int price = root.ShelfShop.SelectedOffer.Price;
            int cashBefore = GameSessionState.CashBalance;
            Assert.That(root.ShelfShop.ConfirmSelection(), Is.True);
            WorldItemFoundScreen receipt = WorldItemFoundScreen.For(root.Player.Interactor);
            Assert.That(receipt.IsPresenting && root.ShelfShop.IsReceiptOpen, Is.True);

            root.ShelfShop.enabled = false;
            yield return null;

            Assert.That(root.ShelfShop.IsOpen, Is.False);
            Assert.That(root.ShelfShop.IsReceiptOpen || receipt.IsPresenting, Is.False);
            Assert.That(GameSessionState.GetInventoryItemCount(boughtItem), Is.EqualTo(1));
            Assert.That(GameSessionState.CashBalance, Is.EqualTo(cashBefore - price));
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
            Assert.That(
                root.Player.PresentationVisibility.RenderersHidden,
                Is.False);
            AssertRendererStates(playerRenderers, playerStates);
            AssertRendererStates(cashierRenderers, cashierStates);
        }

        private static IEnumerator CaptureReceipt(Camera camera)
        {
            AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.SupermarketInterior,
                "Test supermarket - Replenishable offer receipt");
            if (Application.isBatchMode) yield break;
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.SupermarketInterior);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "Test supermarket - Shared receipt Game View.png");
            DateTime previous = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(path);
            yield return WaitUntil(() => File.Exists(path) &&
                File.GetLastWriteTimeUtc(path) > previous && new FileInfo(path).Length > 8,
                "The shared shop receipt Game View capture did not finish.");
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

        private static bool[] CaptureRendererStates(
            IReadOnlyList<Renderer> renderers)
        {
            var states = new bool[renderers.Count];
            for (int index = 0; index < renderers.Count; index++)
            {
                Renderer renderer = renderers[index];
                states[index] = renderer != null && renderer.enabled;
            }

            return states;
        }

        private static void AssertAllRenderersDisabled(
            IReadOnlyList<Renderer> renderers)
        {
            int rendererCount = 0;
            for (int index = 0; index < renderers.Count; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                rendererCount++;
                Assert.That(
                    renderer.enabled,
                    Is.False,
                    $"Renderer '{renderer.name}' stayed visible.");
            }

            Assert.That(rendererCount, Is.GreaterThan(0));
        }

        private static void AssertRendererStates(
            IReadOnlyList<Renderer> renderers,
            IReadOnlyList<bool> expectedStates)
        {
            Assert.That(renderers.Count, Is.EqualTo(expectedStates.Count));
            for (int index = 0; index < renderers.Count; index++)
            {
                Renderer renderer = renderers[index];
                bool actual = renderer != null && renderer.enabled;
                Assert.That(
                    actual,
                    Is.EqualTo(expectedStates[index]),
                    renderer != null
                        ? $"Renderer '{renderer.name}' restored incorrectly."
                        : $"Renderer slot {index} restored incorrectly.");
            }
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
