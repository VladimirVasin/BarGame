using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    [DefaultExecutionOrder(260)]
    [DisallowMultipleComponent]
    public sealed class SupermarketShelfShopController : MonoBehaviour
    {
        private const int MaximumRayHits = 32;
        private const float SelectedScale = 1.08f;
        public const string ReceivedFeedbackKey = "supermarket.shop.received";

        private readonly BarMinigameModalLock modalLock =
            new BarMinigameModalLock();
        private readonly List<SupermarketProductView> products =
            new List<SupermarketProductView>();
        private readonly List<SupermarketShelfView> shelves =
            new List<SupermarketShelfView>();
        private readonly Dictionary<SupermarketProductView, Vector3>
            productBaseScales =
                new Dictionary<SupermarketProductView, Vector3>();
        private readonly RaycastHit[] selectionHits =
            new RaycastHit[MaximumRayHits];

        private SupermarketShelfShopView view;
        private IntoxicationHudView hud;
        private PlayerCameraFollow cameraFollow;
        private Camera targetCamera;
        private PlayerPresentationVisibility playerVisibility;
        private PlayerPresentationVisibility cashierVisibility;
        private IDisposable playerRenderersHidden;
        private IDisposable cashierRenderersHidden;
        private SupermarketShelfView activeShelf;
        private bool cameraWasFixed;
        private Vector3 previousFixedPosition;
        private Quaternion previousFixedRotation;
        private float previousFixedFieldOfView;
        private int inputUnlockFrame;
        private PlayerInteractor activeInteractor;
        private WorldItemFoundScreen receiptScreen;
        private Transform receiptModel;

        public bool IsOpen { get; private set; }
        public bool IsReceiptOpen => receiptScreen != null;
        public int SelectedIndex { get; private set; }
        public string FeedbackKey { get; private set; } = string.Empty;
        public int CashBalance => GameSessionState.CashBalance;
        public SupermarketShelfView ActiveShelf => activeShelf;
        public IReadOnlyList<SupermarketProductView> Products => products;
        public bool CanNavigate => !IsReceiptOpen && CountAvailableProducts() > 1;
        public SupermarketProductView SelectedProduct =>
            products.Count > 0
                ? products[Mathf.Clamp(
                    SelectedIndex,
                    0,
                    products.Count - 1)]
                : null;
        public SupermarketProductOffer SelectedOffer =>
            SelectedProduct != null
                ? SupermarketProductCatalog.GetOffer(
                    SelectedProduct.ItemId)
                : default;

        public void Initialize(
            SupermarketShelfShopView shopView,
            IntoxicationHudView intoxicationHud,
            PlayerCameraFollow follow,
            IReadOnlyList<SupermarketShelfView> shopShelves,
            PlayerPresentationVisibility playerPresentationVisibility = null,
            IRendererPresentation cashierPresentation = null)
        {
            Close();
            view = shopView;
            hud = intoxicationHud;
            cameraFollow = follow;
            playerVisibility = playerPresentationVisibility;
            cashierVisibility = cashierPresentation != null
                ? new PlayerPresentationVisibility(cashierPresentation)
                : null;
            targetCamera = follow != null
                ? follow.GetComponent<Camera>()
                : Camera.main;
            shelves.Clear();
            productBaseScales.Clear();
            if (shopShelves != null)
            {
                for (int shelfIndex = 0;
                     shelfIndex < shopShelves.Count;
                     shelfIndex++)
                {
                    SupermarketShelfView shelf = shopShelves[shelfIndex];
                    if (shelf == null)
                    {
                        continue;
                    }

                    shelves.Add(shelf);

                    for (int productIndex = 0;
                         productIndex < shelf.Products.Count;
                         productIndex++)
                    {
                        CaptureBaseScale(shelf.Products[productIndex]);
                    }
                }
            }

            view?.Initialize(this, targetCamera);
        }

        public bool IsShelfAvailable(SupermarketShelfView shelf)
        {
            if (shelf == null)
            {
                return false;
            }

            for (int index = 0; index < shelf.Products.Count; index++)
            {
                SupermarketProductView product = shelf.Products[index];
                if (product != null &&
                    product.OriginalRoot != null &&
                    product.OriginalRoot.gameObject.activeInHierarchy)
                {
                    return true;
                }
            }

            return false;
        }

        public bool Open(
            SupermarketShelfView shelf,
            PlayerInteractor interactor)
        {
            if (IsOpen ||
                !IsShelfAvailable(shelf) ||
                interactor == null ||
                cameraFollow == null ||
                SceneTransitionService.IsTransitioning)
            {
                return false;
            }

            if (!modalLock.TryCaptureAndDisable(
                    interactor,
                    cameraFollow,
                    hud))
            {
                return false;
            }

            try
            {
                AcquireCharacterVisibility();
                CaptureCameraState();
                activeShelf = shelf;
                RefreshProducts();
                SelectedIndex = 0;
                FeedbackKey = string.Empty;
                inputUnlockFrame = Time.frameCount + 1;
                activeInteractor = interactor;
                IsOpen = true;
                ApplySelectionHighlight();
                ApplySelectedProductCamera();
                Physics.SyncTransforms();
                RetroAudio.Play(RetroSfxId.UiConfirm);
                return true;
            }
            catch
            {
                IsOpen = false;
                RestoreOwnedState();
                throw;
            }
        }

        public bool Select(int index)
        {
            if (!IsOpen || IsReceiptOpen || index < 0 || index >= products.Count)
            {
                return false;
            }

            bool changed = SelectedIndex != index;
            ResetSelectionHighlight();
            SelectedIndex = index;
            FeedbackKey = string.Empty;
            ApplySelectionHighlight();
            ApplySelectedProductCamera();
            Physics.SyncTransforms();
            if (changed)
            {
                RetroAudio.Play(RetroSfxId.UiMove);
            }

            return true;
        }

        public bool MoveSelection(int direction)
        {
            if (!IsOpen || products.Count == 0 || direction == 0 ||
                !CanNavigate)
            {
                return false;
            }

            int step = Math.Sign(direction);
            int next = SelectedIndex + step;
            if (next >= 0 && next < products.Count)
            {
                return Select(next);
            }

            return MoveToAdjacentShelf(step, true);
        }

        public bool ConfirmSelection()
        {
            SupermarketProductView product = SelectedProduct;
            if (!IsOpen || IsReceiptOpen || product == null ||
                Time.frameCount <= inputUnlockFrame)
            {
                return false;
            }

            inputUnlockFrame = Time.frameCount + 1;
            SupermarketPurchaseResult preflight = SupermarketPurchaseRules.Evaluate(
                product.SourceId, product.ItemId, GameSessionState.CashBalance,
                GameSessionState.GetInventoryItemCount(product.ItemId));
            if (!preflight.Succeeded)
            {
                FeedbackKey = GetFailureKey(preflight.Status);
                RetroAudio.Play(RetroSfxId.Bad);
                return false;
            }

            WorldItemFoundScreen screen = WorldItemFoundScreen.For(activeInteractor);
            if (screen == null || !screen.IsInitialized || screen.IsPresenting)
                return false;

            // Prepare the shared receipt before spending cash. Its separate
            // model keeps this replenishable offer and its price tag intact.
            receiptScreen = screen;
            SupermarketPurchaseResult result = default;
            bool committed = false;
            bool presented;
            try
            {
                receiptModel = InventoryItemModelFactory.BuildPreviewModel(
                    product.ItemId, transform);
                if (receiptModel.TryGetComponent(out VodkaBottleView bottle))
                {
                    bottle.SetRemaining(1f);
                    bottle.SetOpened(false);
                }
                presented = screen.TryPresentReceivedWithin(modalLock,
                    activeInteractor, product.ItemId, receiptModel,
                    ReceivedFeedbackKey,
                    () =>
                    {
                        result = GameSessionState.TryPurchaseWorldItem(
                            product.SourceId, product.ItemId);
                        committed = true;
                        return result.Succeeded;
                    },
                    _ => ClearReceipt());
            }
            catch
            {
                ClearReceipt();
                throw;
            }

            if (!presented)
            {
                ClearReceipt();
                if (committed)
                {
                    FeedbackKey = GetFailureKey(result.Status);
                    RetroAudio.Play(RetroSfxId.Bad);
                }
                return false;
            }

            FeedbackKey = string.Empty;
            RetroAudio.Play(RetroSfxId.UiConfirm);
            return true;
        }

        private void ClearReceipt()
        {
            receiptScreen = null;
            if (receiptModel != null)
            {
                receiptModel.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(receiptModel.gameObject);
                else DestroyImmediate(receiptModel.gameObject);
                receiptModel = null;
            }
            // The Close/Escape press belongs to the receipt for its whole
            // rendered frame, including calls through the public shop API.
            inputUnlockFrame = Time.frameCount + 1;
        }

        public void Exit()
        {
            if (!IsOpen)
            {
                return;
            }

            RetroAudio.Play(RetroSfxId.UiCancel);
            Close();
        }

        public void Close()
        {
            bool hadOwnership =
                IsOpen ||
                modalLock.IsLocked ||
                activeShelf != null ||
                playerRenderersHidden != null ||
                cashierRenderersHidden != null;
            IsOpen = false;
            FeedbackKey = string.Empty;
            if (receiptScreen != null) receiptScreen.Abandon();
            ClearReceipt();
            if (hadOwnership)
            {
                RestoreOwnedState();
            }
            else
            {
                modalLock.Restore();
            }
        }

        private void Update()
        {
            if (!IsOpen || IsReceiptOpen || Time.frameCount <= inputUnlockFrame)
            {
                return;
            }

            if (IsCancelPressed())
            {
                Exit();
                return;
            }

            int selectionDelta = ReadSelectionDelta();
            if (selectionDelta != 0)
            {
                MoveSelection(selectionDelta);
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                Vector2 pointer = mouse.position.ReadValue();
                if (view != null &&
                    view.ContainsPointerBlockingWorldSelection(pointer))
                {
                    return;
                }

                if (TryFindPointedProduct(
                        pointer,
                        out SupermarketProductView pointed))
                {
                    Select(products.IndexOf(pointed));
                    return;
                }
            }

            if (IsConfirmPressed())
            {
                ConfirmSelection();
            }
        }

        private void OnDisable()
        {
            Close();
        }

        private void OnDestroy()
        {
            Close();
        }

        private void CaptureCameraState()
        {
            cameraWasFixed = cameraFollow.FixedPoseActive;
            if (!cameraWasFixed)
            {
                return;
            }

            previousFixedPosition = cameraFollow.FixedBasePosition;
            previousFixedRotation = cameraFollow.FixedBaseRotation;
            previousFixedFieldOfView =
                cameraFollow.FixedBaseFieldOfView;
        }

        private void ApplySelectedProductCamera()
        {
            SupermarketProductView selected = SelectedProduct;
            if (activeShelf == null || selected == null)
            {
                return;
            }

            Vector3 lookAt = selected.TryGetWorldBounds(out Bounds bounds)
                ? bounds.center
                : selected.OriginalRoot.position;
            Vector3 direction = lookAt - activeShelf.CameraPosition;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = activeShelf.CameraLookAt -
                            activeShelf.CameraPosition;
            }

            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = Vector3.forward;
            }

            cameraFollow.SetFixedPose(
                activeShelf.CameraPosition,
                Quaternion.LookRotation(direction, Vector3.up),
                activeShelf.CameraFieldOfView);
        }

        private void RestoreOwnedState()
        {
            ResetSelectionHighlight();
            products.Clear();
            activeShelf = null;
            activeInteractor = null;
            if (cameraFollow != null)
            {
                if (cameraWasFixed)
                {
                    cameraFollow.SetFixedPose(
                        previousFixedPosition,
                        previousFixedRotation,
                        previousFixedFieldOfView);
                }
                else
                {
                    cameraFollow.ClearFixedPose();
                    cameraFollow.Snap();
                }
            }

            ReleaseCharacterVisibility();
            modalLock.Restore();
            Physics.SyncTransforms();
        }

        private void AcquireCharacterVisibility()
        {
            playerRenderersHidden = playerVisibility?.AcquireHidden(
                this,
                PlayerPresentationVisibilityScope.Renderers);
            cashierRenderersHidden = cashierVisibility?.AcquireHidden(
                this,
                PlayerPresentationVisibilityScope.Renderers);
        }

        private void ReleaseCharacterVisibility()
        {
            cashierRenderersHidden?.Dispose();
            cashierRenderersHidden = null;
            playerRenderersHidden?.Dispose();
            playerRenderersHidden = null;
        }

        private void RefreshProducts()
        {
            products.Clear();
            if (activeShelf == null)
            {
                return;
            }

            for (int index = 0;
                 index < activeShelf.Products.Count;
                 index++)
            {
                SupermarketProductView product =
                    activeShelf.Products[index];
                if (product == null || product.OriginalRoot == null ||
                    !product.OriginalRoot.gameObject.activeInHierarchy)
                {
                    continue;
                }

                CaptureBaseScale(product);
                products.Add(product);
            }
        }

        private bool MoveToAdjacentShelf(
            int direction,
            bool playAudio)
        {
            if (!IsOpen || activeShelf == null || shelves.Count == 0 ||
                direction == 0)
            {
                return false;
            }

            int activeIndex = shelves.IndexOf(activeShelf);
            if (activeIndex < 0)
            {
                return false;
            }

            int step = Math.Sign(direction);
            for (int offset = 1; offset <= shelves.Count; offset++)
            {
                int candidateIndex =
                    (activeIndex + step * offset + shelves.Count) %
                    shelves.Count;
                SupermarketShelfView candidate = shelves[candidateIndex];
                if (!IsShelfAvailable(candidate))
                {
                    continue;
                }

                ResetSelectionHighlight();
                activeShelf = candidate;
                RefreshProducts();
                SelectedIndex = step > 0 ? 0 : products.Count - 1;
                FeedbackKey = string.Empty;
                ApplySelectionHighlight();
                ApplySelectedProductCamera();
                Physics.SyncTransforms();
                if (playAudio)
                {
                    RetroAudio.Play(RetroSfxId.UiMove);
                }

                return true;
            }

            return false;
        }

        private int CountAvailableProducts()
        {
            int count = 0;
            for (int shelfIndex = 0;
                 shelfIndex < shelves.Count;
                 shelfIndex++)
            {
                SupermarketShelfView shelf = shelves[shelfIndex];
                if (shelf == null)
                {
                    continue;
                }

                for (int productIndex = 0;
                     productIndex < shelf.Products.Count;
                     productIndex++)
                {
                    SupermarketProductView product =
                        shelf.Products[productIndex];
                    if (product != null && product.OriginalRoot != null &&
                        product.OriginalRoot.gameObject.activeInHierarchy)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private void CaptureBaseScale(SupermarketProductView product)
        {
            if (product != null && product.OriginalRoot != null &&
                !productBaseScales.ContainsKey(product))
            {
                productBaseScales.Add(
                    product,
                    product.OriginalRoot.localScale);
            }
        }

        private void ApplySelectionHighlight()
        {
            SupermarketProductView selected = SelectedProduct;
            if (selected != null && selected.OriginalRoot != null &&
                productBaseScales.TryGetValue(
                    selected,
                    out Vector3 baseScale))
            {
                selected.OriginalRoot.localScale =
                    baseScale * SelectedScale;
            }
        }

        private void ResetSelectionHighlight()
        {
            SupermarketProductView selected = SelectedProduct;
            if (selected != null && selected.OriginalRoot != null &&
                productBaseScales.TryGetValue(
                    selected,
                    out Vector3 baseScale))
            {
                selected.OriginalRoot.localScale = baseScale;
            }
        }

        private bool TryFindPointedProduct(
            Vector2 screenPosition,
            out SupermarketProductView product)
        {
            product = null;
            if (targetCamera == null)
            {
                return false;
            }

            int hitCount = Physics.RaycastNonAlloc(
                targetCamera.ScreenPointToRay(screenPosition),
                selectionHits,
                20f,
                ~0,
                QueryTriggerInteraction.Collide);
            float nearestDistance = float.PositiveInfinity;
            for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
            {
                Collider collider = selectionHits[hitIndex].collider;
                if (collider == null ||
                    selectionHits[hitIndex].distance >= nearestDistance)
                {
                    continue;
                }

                for (int productIndex = 0;
                     productIndex < products.Count;
                     productIndex++)
                {
                    SupermarketProductView candidate =
                        products[productIndex];
                    if (candidate.SelectionCollider == collider)
                    {
                        product = candidate;
                        nearestDistance =
                            selectionHits[hitIndex].distance;
                        break;
                    }
                }
            }

            return product != null;
        }

        private static int ReadSelectionDelta()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.upArrowKey.wasPressedThisFrame ||
                    keyboard.leftArrowKey.wasPressedThisFrame ||
                    keyboard.wKey.wasPressedThisFrame ||
                    keyboard.aKey.wasPressedThisFrame)
                {
                    return -1;
                }

                if (keyboard.downArrowKey.wasPressedThisFrame ||
                    keyboard.rightArrowKey.wasPressedThisFrame ||
                    keyboard.sKey.wasPressedThisFrame ||
                    keyboard.dKey.wasPressedThisFrame)
                {
                    return 1;
                }
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad == null)
            {
                return 0;
            }

            if (gamepad.dpad.up.wasPressedThisFrame ||
                gamepad.dpad.left.wasPressedThisFrame ||
                gamepad.leftStick.up.wasPressedThisFrame ||
                gamepad.leftStick.left.wasPressedThisFrame)
            {
                return -1;
            }

            if (gamepad.dpad.down.wasPressedThisFrame ||
                gamepad.dpad.right.wasPressedThisFrame ||
                gamepad.leftStick.down.wasPressedThisFrame ||
                gamepad.leftStick.right.wasPressedThisFrame)
            {
                return 1;
            }

            return 0;
        }

        private static bool IsConfirmPressed()
        {
            return GameInput.WasPressed(
                GameInputAction.Submit, GameInputContext.Menu);
        }

        private static bool IsCancelPressed()
        {
            return GameInput.WasPressed(
                GameInputAction.Cancel, GameInputContext.Menu);
        }

        private static string GetFailureKey(
            SupermarketPurchaseStatus status)
        {
            switch (status)
            {
                case SupermarketPurchaseStatus.InsufficientFunds:
                    return "supermarket.failure.insufficient_funds";
                case SupermarketPurchaseStatus.InventoryFull:
                    return "supermarket.failure.inventory_full";
                case SupermarketPurchaseStatus.InvalidSource:
                    return "supermarket.failure.invalid_source";
                case SupermarketPurchaseStatus.NotOffered:
                default:
                    return "supermarket.failure.not_offered";
            }
        }
    }
}
