using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    public enum RidgeKioskFocus { Vendor, Pie, Beer, Vodka }

    /// <summary>The shared shelf panels focus a real seller or sample; one modal also owns its receipt.</summary>
    [DefaultExecutionOrder(260), DisallowMultipleComponent]
    public sealed class RidgeKioskMenuController : MonoBehaviour, IWorldShelfMenu
    {
        private readonly BarMinigameModalLock modal = new();
        private RidgeKioskStation station;
        private PlayerInteractor player;
        private PlayerCameraFollow follow;
        private Camera camera;
        private IntoxicationHudView hud;
        private FixedCameraPoseLease cameraLease;
        private Vector3 cameraPosition;
        private int inputUnlockFrame;
        private WorldItemFoundScreen receipt;
        private Transform receiptModel;
        private Collider[] cameraBodies = new Collider[16];
        public SupermarketShelfShopView View { get; private set; }
        public bool IsOpen { get; private set; }
        public bool IsReceiptOpen => receipt != null;
        public int SelectedIndex { get; private set; }
        public RidgeKioskFocus SelectedFocus => (RidgeKioskFocus)SelectedIndex;
        public InventoryItemId? SelectedItemId => SelectedIndex == 0 ? null : station.Offers[SelectedIndex - 1].ItemId;
        public Vector3 SelectedFocusPosition => SelectedIndex == 0 ? station.Vendor.Actor.Head.position :
            station.Offers[SelectedIndex - 1].TryGetWorldBounds(out var bounds) ? bounds.center : station.Offers[SelectedIndex - 1].InteractionPosition;
        public SupermarketProductOffer SelectedOffer
        {
            get { SupermarketProductCatalog.TryGetOffer(RetailStock.RidgeKiosk, SelectedItemId ?? default, out var offer); return offer; }
        }
        public string FeedbackKey { get; private set; } = string.Empty;
        public bool IsVisible => IsOpen && !IsReceiptOpen && !PauseMenuController.IsAnyPaused && !GameTimeScaleRuntime.IsPaused;
        public bool CanNavigate => IsVisible;
        public int CashBalance => GameSessionState.CashBalance;
        public string TitleKey => "kiosk.menu.title";
        public string NameKey => SelectedIndex == 0 ? "kiosk.menu.vendor" : SelectedOffer.NameLocalizationKey;
        public string DescriptionKey => SelectedIndex == 0 ? string.Empty : SelectedOffer.DescriptionLocalizationKey;
        public string ConfirmKey => SelectedIndex == 0 ? (station.Vendor.HasDialogue ? "kiosk.menu.talk" : string.Empty) : "supermarket.shop.buy";
        public int? Price => SelectedIndex == 0 ? null : SelectedOffer.Price;

        internal void Initialize(RidgeKioskStation owner)
        {
            station = owner;
            View = gameObject.AddComponent<SupermarketShelfShopView>();
        }

        public bool Open(RidgeKioskStation owner, PlayerInteractor interactor)
        {
            if (IsOpen || !isActiveAndEnabled || owner != station || !interactor || !interactor.InputEnabled ||
                interactor.InteractKeyClaimed || CounterMenuInput.IsBlockedByOtherUi() || !station.CanReach(interactor)) return false;
            if (!camera) camera = Camera.main;
            if (!follow && camera) follow = camera.GetComponent<PlayerCameraFollow>();
            if (!hud) hud = FindAnyObjectByType<IntoxicationHudView>();
            if (!follow || !follow.isActiveAndEnabled || !TryResolveCamera(interactor, out cameraPosition) ||
                !modal.TryCaptureAndDisable(interactor, follow, hud)) return false;
            try
            {
                cameraLease = new FixedCameraPoseLease(follow);
                player = interactor; SelectedIndex = 0; FeedbackKey = string.Empty;
                inputUnlockFrame = Time.frameCount + 1; IsOpen = true;
                View.Initialize(this, camera);
                FocusCamera(); Physics.SyncTransforms();
                RetroAudio.Play(RetroSfxId.UiConfirm);
                return true;
            }
            catch { Close(); throw; }
        }

        public bool Select(int index)
        {
            if (!IsVisible || index < 0 || index >= 4) return false;
            bool changed = SelectedIndex != index;
            SelectedIndex = index; FeedbackKey = string.Empty;
            FocusCamera();
            if (changed) RetroAudio.Play(RetroSfxId.UiMove);
            return true;
        }
        public bool MoveSelection(int direction) => direction != 0 && Select((SelectedIndex + (direction > 0 ? 1 : 3)) % 4);

        public bool ConfirmSelection()
        {
            if (!IsVisible || Time.frameCount <= inputUnlockFrame || !player || !station.CanReach(player)) return false;
            inputUnlockFrame = Time.frameCount + 1;
            if (SelectedIndex == 0)
            {
                if (!station.Vendor.HasDialogue) return false;
                var customer = player;
                Close();
                return station.Vendor.TryStartFromMenu(customer);
            }
            var offer = station.Offers[SelectedIndex - 1];
            var preflight = SupermarketPurchaseRules.Evaluate(offer.SourceId, offer.ItemId, CashBalance,
                GameSessionState.GetInventoryItemCount(offer.ItemId), RetailStock.RidgeKiosk);
            if (!preflight.Succeeded) return Refuse(preflight.Status);
            var screen = WorldItemFoundScreen.For(player);
            if (!screen || !screen.IsInitialized || screen.IsPresenting) return false;
            receipt = screen;
            SupermarketPurchaseResult result = default;
            bool committed = false;
            try
            {
                receiptModel = InventoryItemModelFactory.BuildPreviewModel(offer.ItemId, transform);
                if (receiptModel.TryGetComponent(out VodkaBottleView bottle)) { bottle.SetRemaining(1f); bottle.SetOpened(false); }
                bool presented = screen.TryPresentReceivedWithin(modal, player, offer.ItemId, receiptModel,
                    SupermarketShelfShopController.ReceivedFeedbackKey,
                    () => { committed = true; result = GameSessionState.TryPurchaseWorldItem(offer.SourceId, offer.ItemId, RetailStock.RidgeKiosk); return result.Succeeded; },
                    _ => ClearReceipt());
                if (!presented)
                {
                    ClearReceipt();
                    if (committed) Refuse(result.Status);
                    return false;
                }
                FeedbackKey = string.Empty;
                RetroAudio.Play(RetroSfxId.UiConfirm);
                return true;
            }
            catch { ClearReceipt(); throw; }
        }

        private bool Refuse(SupermarketPurchaseStatus status)
        {
            FeedbackKey = SupermarketShelfShopController.GetFailureKey(status);
            RetroAudio.Play(RetroSfxId.Bad);
            return false;
        }
        private void ClearReceipt()
        {
            receipt = null;
            if (receiptModel)
            {
                receiptModel.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(receiptModel.gameObject); else DestroyImmediate(receiptModel.gameObject);
                receiptModel = null;
            }
            inputUnlockFrame = Time.frameCount + 1;
        }
        public void Exit() { if (IsOpen && !GameTimeScaleRuntime.IsPaused) { RetroAudio.Play(RetroSfxId.UiCancel); Close(); } }
        public void Close()
        {
            IsOpen = false;
            if (receipt) receipt.Abandon();
            ClearReceipt();
            cameraLease?.Dispose(); cameraLease = null;
            modal.Restore();
            player = null; FeedbackKey = string.Empty;
        }

        public bool TryGetSelectionBounds(out Bounds bounds)
        {
            if (SelectedIndex == 0) { bounds = new Bounds(station.Vendor.Actor.Head.position + Vector3.up * .08f, new Vector3(.32f, .42f, .30f)); return true; }
            return station.Offers[SelectedIndex - 1].TryGetWorldBounds(out bounds);
        }
        private void FocusCamera()
        {
            Vector3 direction = SelectedFocusPosition - cameraPosition;
            Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
            float tangent = Mathf.Tan(14f * Mathf.Deg2Rad);
            if (TryGetSelectionBounds(out Bounds bounds))
            {
                Quaternion inverse = Quaternion.Inverse(rotation);
                for (int mask = 0; mask < 8; mask++)
                {
                    Vector3 corner = new Vector3((mask & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (mask & 2) == 0 ? bounds.min.y : bounds.max.y, (mask & 4) == 0 ? bounds.min.z : bounds.max.z);
                    Vector3 view = inverse * (corner - cameraPosition);
                    float depth = Mathf.Max(.01f, view.z);
                    tangent = Mathf.Max(tangent, Mathf.Max(Mathf.Abs(view.y) / (depth * .45f),
                        Mathf.Abs(view.x) / (depth * .8f * Mathf.Max(.5f, camera.aspect))));
                }
            }
            float fov = Mathf.Clamp(2f * Mathf.Atan(tangent) * Mathf.Rad2Deg + 2f, 28f, 75f);
            follow.SetFixedPose(cameraPosition, rotation, fov);
        }

        private bool TryResolveCamera(PlayerInteractor interactor, out Vector3 position)
        {
            Vector3 eye = interactor.transform.position + Vector3.up * 1.55f;
            Vector3 direction = Vector3.ProjectOnPlane(station.InteractionPosition - eye, Vector3.up).normalized;
            position = eye + direction * .58f;
            int count;
            while ((count = Physics.OverlapSphereNonAlloc(position, .09f, cameraBodies, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore)) == cameraBodies.Length) Array.Resize(ref cameraBodies, cameraBodies.Length * 2);
            for (int index = 0; index < count; index++)
                if (!cameraBodies[index].transform.IsChildOf(interactor.transform)) return false;
            return true;
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (SceneTransitionService.IsTransitioning || !station || !station.isActiveAndEnabled || !player) { Close(); return; }
            if (!IsVisible || Time.frameCount <= inputUnlockFrame) return;
            if (!station.CanReach(player)) { Close(); return; }
            if (GameInput.WasPressed(GameInputAction.Cancel, GameInputContext.Menu)) { Exit(); return; }
            int delta = GameInput.ReadMenuSelectionDelta(GameInputContext.Menu);
            if (delta != 0) { MoveSelection(delta); return; }
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame &&
                !View.ContainsPointerBlockingWorldSelection(Mouse.current.position.ReadValue()))
            {
                Vector2 pointer = Mouse.current.position.ReadValue();
                float closest = 90f * 90f; int selected = -1;
                for (int index = 0; index < 4; index++)
                {
                    Vector3 point = camera.WorldToScreenPoint(index == 0 ? station.Vendor.Actor.Head.position : station.Offers[index - 1].InteractionPosition);
                    if (point.z <= camera.nearClipPlane) continue;
                    float distance = ((Vector2)point - pointer).sqrMagnitude;
                    if (distance < closest) { closest = distance; selected = index; }
                }
                if (selected >= 0) { Select(selected); return; }
            }
            if (GameInput.WasPressed(GameInputAction.Submit, GameInputContext.Menu)) ConfirmSelection();
        }
        private void OnDisable() => Close();
        private void OnDestroy() => Close();
        private void LateUpdate() { if (IsVisible && SelectedIndex == 0) FocusCamera(); }
    }
}
