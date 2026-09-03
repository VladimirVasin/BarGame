using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using BarPromenade.Runtime.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    /// <summary>
    /// Owns the ordinary drink transaction and, in a real bar interior, the
    /// modal seated first-person bottle browser and serving presentation.
    /// The legacy Initialize overload is retained for isolated modal callers;
    /// production bars always supply a generated BarDrinkServiceView.
    /// </summary>
    [DefaultExecutionOrder(260)]
    [DisallowMultipleComponent]
    public sealed class BarDrinkShopController : MonoBehaviour
    {
        private const int MaximumRayHits = 48;

        private readonly BarMinigameModalLock modalLock =
            new BarMinigameModalLock();
        private readonly RaycastHit[] selectionHits =
            new RaycastHit[MaximumRayHits];

        private Renderer[] sceneMarkerRenderers = Array.Empty<Renderer>();
        private bool[] previousSceneMarkerStates = Array.Empty<bool>();

        private BarDrinkShopView view;
        private IntoxicationHudView hud;
        private PlayerCameraFollow cameraFollow;
        private PlayerRuntime player;
        private BarDrinkServiceView serviceView;
        private BarDrinkServicePlan servicePlan;
        private BarDrinkServiceTimeline timeline =
            new BarDrinkServiceTimeline();
        private BarDrinkFirstPersonArms firstPersonArms;
        private CounterSeatView counterSeatView;
        private BarDrinkMenuPresentation menuPresentation;
        private CounterMenuModel counterMenuModel;
        private CounterMenuHintView counterMenuHint;
        private Camera targetCamera;
        private BarDrinkBottleView hoveredBottle;
        private BarDrinkPresentation activePresentation;
        private Vector3 cameraStartPosition;
        private Quaternion cameraStartRotation;
        private float cameraStartFieldOfView;
        private Vector3 cameraControlPosition;
        private Vector3 cameraTargetPosition;
        private Vector3 depthOfFieldFocusPoint;
        private Quaternion cameraTargetRotation;
        private float cameraTargetFieldOfView;
        private bool cameraWasFixed;
        private Vector3 previousFixedPosition;
        private Quaternion previousFixedRotation;
        private float previousFixedFieldOfView;
        private Vector3 bottleStartPosition;
        private Quaternion bottleStartRotation;
        private Vector3 vesselBaseScale = Vector3.one;

        /// <summary>
        /// Where the vessel enters from, in service-plan local space:
        /// down the counter past the left edge of the seated frame,
        /// flat on the brass — the bartender's slide, not a spawn.
        /// </summary>
        public static readonly Vector3 VesselSlideEntryOffset =
            new Vector3(-1.9f, 0f, 0f);
        private int inputUnlockFrame;
        private bool hasPhysicalPresentation;
        private bool playerVisualStateCaptured;
        private bool playerVisualHidden;
        private IDisposable playerVisualHideLease;
        private bool sceneMarkerStateCaptured;
        private bool purchaseCommitted;
        private bool clinkPlayed;
        private bool pourPlayed;
        private bool gulpPlayed;
        private bool menuDeliveryWhileBrowsing;
        private float menuDeliveryElapsedSeconds;

        public event Action<BarDrinkShopController> Closed;

        public bool IsOpen { get; private set; }
        public int SelectedIndex { get; private set; }
        public string FeedbackKey { get; private set; } = string.Empty;
        public IReadOnlyList<BarDrinkOffer> Offers =>
            BarDrinkCatalog.Offers;
        public int CashBalance => GameSessionState.CashBalance;
        public int IntoxicationLevel => GameSessionState.IntoxicationLevel;
        public BarDrinkOffer SelectedOffer =>
            Offers.Count > 0
                ? Offers[Mathf.Clamp(SelectedIndex, 0, Offers.Count - 1)]
                : default;
        public BarDrinkServicePhase Phase =>
            hasPhysicalPresentation && timeline != null
                ? timeline.Phase
                : IsOpen
                    ? BarDrinkServicePhase.Browsing
                    : BarDrinkServicePhase.Closed;
        public bool IsBrowsing =>
            IsOpen &&
            (!hasPhysicalPresentation ||
             (timeline.IsBrowsing &&
              (!UsesPhysicalMenu ||
               counterMenuModel.State == CounterMenuState.Open)));
        public bool IsServing =>
            IsOpen && hasPhysicalPresentation && timeline.IsCommitted;
        public bool PurchaseCommitted => purchaseCommitted;
        public BarDrinkServiceTimeline Timeline => timeline;
        public BarDrinkServiceView ServiceView => serviceView;
        public BarDrinkFirstPersonArms FirstPersonArms => firstPersonArms;
        public BarDrinkBottleView HoveredBottle => hoveredBottle;
        public CounterSeatView CounterSeatView => counterSeatView;
        public bool UsesCounterSeatView => counterSeatView != null;
        public BarDrinkMenuPresentation MenuPresentation =>
            menuPresentation;
        public CounterMenuState MenuState => counterMenuModel?.State ??
            CounterMenuState.Hidden;
        public bool UsesPhysicalMenu => counterSeatView != null &&
            menuPresentation != null && counterMenuModel != null;
        public bool CanExitPhysicalMenu => UsesPhysicalMenu && IsOpen &&
            !IsServing && timeline != null && timeline.CanCancel;

        public void Initialize(
            BarDrinkShopView shopView,
            IntoxicationHudView intoxicationHud,
            PlayerCameraFollow follow)
        {
            Close();
            view = shopView;
            hud = intoxicationHud;
            cameraFollow = follow;
            targetCamera = follow != null
                ? follow.GetComponent<Camera>()
                : Camera.main;
            hasPhysicalPresentation = false;
            serviceView = null;
            servicePlan = null;
            firstPersonArms = null;
            counterSeatView = null;
            menuPresentation = null;
            counterMenuModel = null;
            counterMenuHint?.Hide();
            counterMenuHint = null;
            sceneMarkerRenderers = Array.Empty<Renderer>();
            previousSceneMarkerStates = Array.Empty<bool>();
            sceneMarkerStateCaptured = false;
            timeline = new BarDrinkServiceTimeline();
            view?.Initialize(this);
        }

        /// <summary>
        /// Hands camera and world-hero visibility ownership to a physical
        /// counter seat. The shop continues to own the transaction, modal UI,
        /// camera-local arms and drink-service timeline.
        /// </summary>
        public void ConfigureSeatedView(CounterSeatView seatedView)
        {
            if (IsOpen || modalLock.IsLocked)
            {
                throw new InvalidOperationException(
                    "The bar counter seat cannot change while the shop is open.");
            }

            if (seatedView == null || !seatedView.IsInitialized)
            {
                throw new ArgumentException(
                    "The bar shop requires an initialized counter seat view.",
                    nameof(seatedView));
            }

            counterSeatView = seatedView;
            if (menuPresentation == null || !menuPresentation.IsConfigured ||
                counterMenuModel == null)
            {
                throw new InvalidOperationException(
                    "A seated bar requires the authored physical menu.");
            }

            counterMenuHint = CounterMenuHintView.Create(
                transform,
                "Bar Drink Menu Hint",
                MountainRoadCafeMenuHintView.SelectHintKey,
                MountainRoadCafeMenuHintView.OrderHintKey);
        }

        public void ConfigureMenuCarrier(Transform carrier)
        {
            if (menuPresentation == null)
            {
                throw new InvalidOperationException(
                    "The physical bar menu is not initialized.");
            }

            menuPresentation.ConfigureCarrier(carrier);
        }

        public void Initialize(
            BarDrinkShopView shopView,
            IntoxicationHudView intoxicationHud,
            PlayerCameraFollow follow,
            PlayerRuntime playerRuntime,
            BarDrinkServiceView physicalPresentation)
        {
            if (playerRuntime.GameObject == null ||
                playerRuntime.Interactor == null ||
                playerRuntime.Visual == null)
            {
                throw new ArgumentException(
                    "Physical drink service requires an initialized player.",
                    nameof(playerRuntime));
            }

            if (physicalPresentation == null ||
                physicalPresentation.Plan == null)
            {
                throw new ArgumentNullException(
                    nameof(physicalPresentation));
            }

            Initialize(shopView, intoxicationHud, follow);
            player = playerRuntime;
            serviceView = physicalPresentation;
            servicePlan = physicalPresentation.Plan;
            menuPresentation = physicalPresentation.MenuPresentation;
            if (menuPresentation == null || !menuPresentation.IsConfigured)
            {
                throw new ArgumentException(
                    "Physical drink service requires an authored menu.",
                    nameof(physicalPresentation));
            }

            var menuItemIds = new string[BarDrinkCatalog.Offers.Count];
            for (int index = 0; index < menuItemIds.Length; index++)
            {
                menuItemIds[index] =
                    BarDrinkCatalog.Offers[index].NameKey;
            }

            counterMenuModel = new CounterMenuModel(menuItemIds);
            targetCamera = follow != null
                ? follow.GetComponent<Camera>()
                : Camera.main;
            if (targetCamera == null)
            {
                throw new InvalidOperationException(
                    "Physical drink service requires a camera.");
            }

            firstPersonArms =
                GetComponent<BarDrinkFirstPersonArms>();
            if (firstPersonArms == null)
            {
                firstPersonArms =
                    gameObject.AddComponent<BarDrinkFirstPersonArms>();
            }

            firstPersonArms.Initialize(targetCamera);
            serviceView.ResetPresentation();
            hasPhysicalPresentation = true;
        }

        public void ConfigureSceneMarkers(params Renderer[] markerRenderers)
        {
            if (IsOpen || sceneMarkerStateCaptured)
            {
                throw new InvalidOperationException(
                    "Bar drink scene markers cannot change while the menu is open.");
            }

            if (markerRenderers == null || markerRenderers.Length == 0)
            {
                sceneMarkerRenderers = Array.Empty<Renderer>();
                previousSceneMarkerStates = Array.Empty<bool>();
                return;
            }

            var unique = new HashSet<Renderer>();
            var copy = new Renderer[markerRenderers.Length];
            for (int index = 0; index < markerRenderers.Length; index++)
            {
                Renderer marker = markerRenderers[index];
                if (marker == null || !unique.Add(marker))
                {
                    throw new ArgumentException(
                        "Bar drink scene markers must be non-null and unique.",
                        nameof(markerRenderers));
                }

                copy[index] = marker;
            }

            sceneMarkerRenderers = copy;
            previousSceneMarkerStates = new bool[copy.Length];
        }

        public bool Open(PlayerInteractor interactor)
        {
            if (IsOpen ||
                interactor == null ||
                Offers.Count == 0 ||
                (counterSeatView != null &&
                 !counterSeatView.IsFirstPerson) ||
                (counterSeatView != null &&
                 CounterMenuInput.IsBlockedByOtherUi()) ||
                SceneTransitionService.IsTransitioning)
            {
                return false;
            }

            if (counterSeatView == null &&
                !modalLock.TryCaptureAndDisable(
                    interactor,
                    cameraFollow,
                    hud))
            {
                return false;
            }

            try
            {
                CaptureAndHideSceneMarkers();
                SelectedIndex = 0;
                FeedbackKey = string.Empty;
                inputUnlockFrame = Time.frameCount + 1;
                purchaseCommitted = false;
                ResetCueState();
                if (hasPhysicalPresentation)
                {
                    if (counterSeatView == null)
                    {
                        CaptureCameraPath();
                        CapturePlayerVisualState();
                    }
                    else
                    {
                        BeginServiceDepthOfField();
                    }

                    serviceView.ResetPresentation();
                    serviceView.SelectBottle(SelectedOffer.DrinkId);
                    if (UsesPhysicalMenu)
                    {
                        BeginPhysicalMenuDelivery(
                            immediate: false,
                            whileBrowsing: false);
                    }

                    timeline.Reset();
                    if (!timeline.BeginOpen())
                    {
                        RestoreOwnedState();
                        return false;
                    }

                    Physics.SyncTransforms();
                }

                IsOpen = true;
                ApplyCurrentPresentation();
                RetroAudio.Play(RetroSfxId.UiConfirm);
                return true;
            }
            catch
            {
                IsOpen = false;
                timeline?.Reset();
                RestoreOwnedState();
                throw;
            }
        }

        public bool Select(int index)
        {
            if (!IsBrowsing || index < 0 || index >= Offers.Count)
            {
                return false;
            }

            bool changed = SelectedIndex != index;
            if (UsesPhysicalMenu &&
                (counterMenuModel.State != CounterMenuState.Open ||
                 !counterMenuModel.Select(index)))
            {
                return false;
            }

            SelectedIndex = index;
            FeedbackKey = string.Empty;
            counterMenuHint?.ClearStatus();
            if (hasPhysicalPresentation)
            {
                serviceView.SelectBottle(SelectedOffer.DrinkId);
            }

            menuPresentation?.SetSelection(SelectedIndex, false);

            if (changed)
            {
                RetroAudio.Play(RetroSfxId.UiMove);
            }

            return true;
        }

        public bool MoveSelection(int direction)
        {
            if (!IsBrowsing || Offers.Count == 0 || direction == 0)
            {
                return false;
            }

            int offset = Math.Sign(direction);
            int nextIndex =
                (SelectedIndex + offset + Offers.Count) % Offers.Count;
            return Select(nextIndex);
        }

        public bool ConfirmSelection()
        {
            if (!IsBrowsing || Offers.Count == 0 ||
                (UsesPhysicalMenu &&
                 counterMenuModel.State != CounterMenuState.Open))
            {
                return false;
            }

            DrinkPurchaseResult result =
                GameSessionState.TryPurchaseDrink(SelectedOffer.DrinkId);
            if (!result.Succeeded)
            {
                FeedbackKey = GetFailureKey(result.Status);
                counterMenuHint?.ShowStatus(FeedbackKey);
                RetroAudio.Play(RetroSfxId.Bad);
                return false;
            }

            if (UsesPhysicalMenu)
            {
                if (!counterMenuModel.Confirm())
                {
                    throw new InvalidOperationException(
                        "A purchased bar-menu selection could not commit.");
                }

                menuPresentation.SetSelection(SelectedIndex, true);
                counterMenuModel.BeginRetrieval();
                menuPresentation.BeginRetrieval();
                menuDeliveryWhileBrowsing = false;
                menuDeliveryElapsedSeconds = 0f;
                counterSeatView.EndMenuFocus();
                counterMenuHint.Hide();
            }

            purchaseCommitted = true;
            FeedbackKey = string.Empty;
            ResetCueState();
            if (!hasPhysicalPresentation)
            {
                RetroAudio.Play(RetroSfxId.DrinkGulp);
                RetroAudio.Play(RetroSfxId.UiConfirm);
                Close();
                return true;
            }

            if (!timeline.Confirm())
            {
                throw new InvalidOperationException(
                    "A committed drink purchase could not start service.");
            }

            activePresentation =
                BarDrinkPresentationCatalog.Get(SelectedOffer.DrinkId);
            BarDrinkBottleView bottle = serviceView.SelectedBottle;
            if (bottle == null ||
                !serviceView.ShowVesselForDrink(SelectedOffer.DrinkId))
            {
                throw new InvalidOperationException(
                    "The selected drink has no physical bottle or vessel.");
            }

            bottleStartPosition = bottle.transform.position;
            bottleStartRotation = bottle.transform.rotation;
            vesselBaseScale = serviceView.ActiveVessel.transform.localScale;
            inputUnlockFrame = int.MaxValue;
            RetroAudio.Play(RetroSfxId.UiConfirm);
            ApplyCurrentPresentation();
            return true;
        }

        public DrinkPurchaseResult PreviewSelection()
        {
            BarDrinkOffer offer = SelectedOffer;
            return DrinkPurchaseRules.Evaluate(
                offer.DrinkId,
                GameSessionState.CashBalance,
                GameSessionState.IntoxicationLevel,
                GameSessionState.LastAlcoholicDrink,
                GameSessionState.DrinksConsumed);
        }

        public void Exit()
        {
            if (!IsOpen)
            {
                return;
            }

            if (!hasPhysicalPresentation)
            {
                RetroAudio.Play(RetroSfxId.UiCancel);
                Close();
                return;
            }

            if (timeline.Cancel())
            {
                FeedbackKey = string.Empty;
                if (UsesPhysicalMenu &&
                    counterMenuModel.BeginRetrieval())
                {
                    menuPresentation.BeginRetrieval();
                    menuDeliveryWhileBrowsing = false;
                    menuDeliveryElapsedSeconds = 0f;
                    counterSeatView.EndMenuFocus();
                    counterMenuHint.Hide();
                }

                RetroAudio.Play(RetroSfxId.UiCancel);
                ApplyCurrentPresentation();
            }
        }

        public void Cancel()
        {
            Exit();
        }

        /// <summary>
        /// Immediate lifecycle/debug cleanup. A committed purchase is never
        /// refunded; only presentation ownership is released.
        /// </summary>
        public void Close()
        {
            bool wasOpen = IsOpen;
            bool hadOwnership =
                IsOpen ||
                modalLock.IsLocked ||
                playerVisualStateCaptured ||
                sceneMarkerStateCaptured;
            IsOpen = false;
            FeedbackKey = string.Empty;
            timeline?.Reset();
            if (hadOwnership)
            {
                RestoreOwnedState();
            }
            else
            {
                serviceView?.ResetPresentation();
                firstPersonArms?.Hide();
                modalLock.Restore();
            }

            if (wasOpen)
            {
                Closed?.Invoke(this);
            }
        }

        public void AdvancePresentation(float unscaledDeltaTime)
        {
            if (!IsOpen || !hasPhysicalPresentation)
            {
                return;
            }

            bool wasCommitted = timeline.IsCommitted;
            bool wasBrowsingMenuDelivery =
                menuDeliveryWhileBrowsing;
            timeline.Advance(unscaledDeltaTime);
            PlayCrossedCues();
            if (wasCommitted &&
                !timeline.IsCommitted &&
                timeline.IsBrowsing)
            {
                CompleteOrderPresentation();
            }

            if (wasBrowsingMenuDelivery)
            {
                AdvanceBrowsingMenuDelivery(unscaledDeltaTime);
            }

            ApplyCurrentPresentation();
            if (timeline.Phase == BarDrinkServicePhase.Closed)
            {
                IsOpen = false;
                RestoreOwnedState();
                Closed?.Invoke(this);
            }
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            if (hasPhysicalPresentation)
            {
                if (UsesPhysicalMenu)
                {
                    UpdatePhysicalMenuHint();
                }

                if (!timeline.IsCommitted &&
                    !timeline.IsBrowsing &&
                    Time.frameCount > inputUnlockFrame &&
                    (!UsesPhysicalMenu ||
                     !CounterMenuInput.IsBlockedByOtherUi()) &&
                    ReadCancelPressed())
                {
                    Cancel();
                }

                if (timeline.IsBrowsing &&
                    Time.frameCount > inputUnlockFrame &&
                    (!UsesPhysicalMenu ||
                     !CounterMenuInput.IsBlockedByOtherUi()))
                {
                    HandleBrowsingInput();
                }

                AdvancePresentation(Time.unscaledDeltaTime);
                return;
            }

            if (Time.frameCount <= inputUnlockFrame)
            {
                return;
            }

            if (IsCancelPressed())
            {
                Cancel();
                return;
            }

            int selectionDelta = ReadSelectionDelta();
            if (selectionDelta != 0)
            {
                MoveSelection(selectionDelta);
                return;
            }

            if (IsConfirmPressed())
            {
                ConfirmSelection();
            }
        }

        private void LateUpdate()
        {
            if (IsOpen && hasPhysicalPresentation)
            {
                ApplyCurrentPresentation();
            }
        }

        private void OnDisable()
        {
            Close();
        }

        private void OnDestroy()
        {
            Close();
            Closed = null;
        }

        private void HandleBrowsingInput()
        {
            if (UsesPhysicalMenu)
            {
                if (CounterMenuInput.WasCancelPressed())
                {
                    Cancel();
                    return;
                }

                int menuSelectionDelta =
                    CounterMenuInput.ReadSelectionDelta();
                if (menuSelectionDelta != 0)
                {
                    MoveSelection(menuSelectionDelta);
                    return;
                }

                if (CounterMenuInput.WasConfirmPressed())
                {
                    ConfirmSelection();
                }

                return;
            }

            RefreshPointerHover();
            if (IsCancelPressed())
            {
                Cancel();
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
                if (TryFindPointedBottle(
                        mouse.position.ReadValue(),
                        out BarDrinkBottleView pointed))
                {
                    Select(FindOfferIndex(pointed.DrinkId));
                }
            }

            if (IsConfirmPressed())
            {
                ConfirmSelection();
            }
        }

        private bool ReadCancelPressed()
        {
            return UsesPhysicalMenu
                ? CounterMenuInput.WasCancelPressed()
                : IsCancelPressed();
        }

        private void UpdatePhysicalMenuHint()
        {
            if (counterMenuHint == null)
            {
                return;
            }

            if (!IsBrowsing ||
                CounterMenuInput.IsBlockedByOtherUi())
            {
                counterMenuHint.Hide();
                return;
            }

            if (!string.IsNullOrEmpty(FeedbackKey))
            {
                counterMenuHint.ShowStatus(FeedbackKey);
                return;
            }

            counterMenuHint.ClearStatus();
            counterMenuHint.Show();
        }

        private void RefreshPointerHover()
        {
            Mouse mouse = Mouse.current;
            BarDrinkBottleView next = null;
            if (mouse != null)
            {
                TryFindPointedBottle(mouse.position.ReadValue(), out next);
            }

            if (next == hoveredBottle)
            {
                return;
            }

            if (hoveredBottle != null &&
                hoveredBottle != serviceView.SelectedBottle)
            {
                hoveredBottle.SetSelectionHighlight(0f);
            }

            hoveredBottle = next;
            if (hoveredBottle != null &&
                hoveredBottle != serviceView.SelectedBottle)
            {
                hoveredBottle.SetSelectionHighlight(0.62f);
            }
        }

        private bool TryFindPointedBottle(
            Vector2 screenPosition,
            out BarDrinkBottleView bottle)
        {
            bottle = null;
            if (targetCamera == null || serviceView == null)
            {
                return false;
            }

            Ray ray = targetCamera.ScreenPointToRay(screenPosition);
            int hitCount = Physics.RaycastNonAlloc(
                ray,
                selectionHits,
                20f,
                ~0,
                QueryTriggerInteraction.Collide);
            float nextDistance = float.PositiveInfinity;
            int nextIndex = -1;
            for (int index = 0; index < hitCount; index++)
            {
                if (selectionHits[index].collider != null &&
                    selectionHits[index].distance < nextDistance)
                {
                    nextDistance = selectionHits[index].distance;
                    nextIndex = index;
                }
            }

            while (nextIndex >= 0)
            {
                Collider collider = selectionHits[nextIndex].collider;
                selectionHits[nextIndex] = default;
                if (serviceView.TryGetBottle(collider, out bottle))
                {
                    return true;
                }

                if (collider != null && !collider.isTrigger)
                {
                    bottle = null;
                    return false;
                }

                nextDistance = float.PositiveInfinity;
                nextIndex = -1;
                for (int index = 0; index < hitCount; index++)
                {
                    if (selectionHits[index].collider != null &&
                        selectionHits[index].distance < nextDistance)
                    {
                        nextDistance = selectionHits[index].distance;
                        nextIndex = index;
                    }
                }
            }

            return false;
        }

        private int FindOfferIndex(DrinkId drinkId)
        {
            for (int index = 0; index < Offers.Count; index++)
            {
                if (Offers[index].DrinkId == drinkId)
                {
                    return index;
                }
            }

            return -1;
        }

        private void CaptureCameraPath()
        {
            cameraStartPosition = targetCamera.transform.position;
            cameraStartRotation = targetCamera.transform.rotation;
            cameraStartFieldOfView = targetCamera.fieldOfView;
            cameraWasFixed =
                cameraFollow != null && cameraFollow.FixedPoseActive;
            if (cameraWasFixed)
            {
                previousFixedPosition = cameraFollow.FixedBasePosition;
                previousFixedRotation = cameraFollow.FixedBaseRotation;
                previousFixedFieldOfView = cameraFollow.FixedBaseFieldOfView;
            }

            Transform reference = serviceView.transform;
            cameraTargetPosition =
                reference.TransformPoint(servicePlan.CameraPosition);
            cameraTargetRotation =
                reference.rotation * servicePlan.CameraRotation;
            cameraTargetFieldOfView = servicePlan.CameraFieldOfView;
            cameraControlPosition = Vector3.Lerp(
                    cameraStartPosition,
                    cameraTargetPosition,
                    0.52f) +
                Vector3.up * 0.18f +
                cameraStartRotation * Vector3.right * 0.08f;

            BeginServiceDepthOfField();
        }

        private void BeginServiceDepthOfField()
        {
            // The counter's pour spot is what the shot studies; the
            // shelves and the hall behind melt into bokeh.
            Transform reference = serviceView.transform;
            depthOfFieldFocusPoint = reference.TransformPoint(
                servicePlan.BottlePourPose.Position);
            CinematicDepthOfField.Begin(
                Vector3.Distance(
                    targetCamera.transform.position,
                    depthOfFieldFocusPoint),
                4f);
        }

        private void ApplyCurrentPresentation()
        {
            if (!IsOpen || !hasPhysicalPresentation || timeline == null)
            {
                return;
            }

            BarDrinkServiceFrame frame = timeline.CurrentFrame;
            ApplyPhysicalMenuPresentation(frame);
            ApplyCamera(frame.CameraBlend);
            // The bartender owns the bottle now; the hero's right arm
            // never grips, only the left-hand drink lift remains his.
            firstPersonArms.ApplyPresentation(
                frame.ArmsVisibility,
                0f,
                frame.DrinkLift);
            ApplyPlayerVisualForFrame(frame);
            ApplyBottlePresentation(frame);
            ApplyVesselPresentation(frame);
        }

        private void ApplyPhysicalMenuPresentation(
            BarDrinkServiceFrame frame)
        {
            if (!UsesPhysicalMenu)
            {
                return;
            }

            switch (frame.Phase)
            {
                case BarDrinkServicePhase.CameraApproach:
                    if (counterMenuModel.State ==
                        CounterMenuState.Delivering)
                    {
                        menuPresentation.EvaluateDelivery(
                            frame.PhaseProgress);
                    }
                    break;
                case BarDrinkServicePhase.Browsing:
                    if (counterMenuModel.State ==
                            CounterMenuState.Delivering &&
                        !menuDeliveryWhileBrowsing)
                    {
                        CompletePhysicalMenuDelivery();
                    }
                    else if (counterMenuModel.State ==
                             CounterMenuState.Closed)
                    {
                        BeginPhysicalMenuDelivery(
                            immediate: false,
                            whileBrowsing: true);
                    }
                    break;
                case BarDrinkServicePhase.BottlePickup:
                case BarDrinkServicePhase.CameraReturn:
                    if (counterMenuModel.State ==
                        CounterMenuState.Retrieving)
                    {
                        menuPresentation.EvaluateRetrieval(
                            frame.PhaseProgress);
                    }
                    break;
                default:
                    if (counterMenuModel.State ==
                        CounterMenuState.Retrieving)
                    {
                        CompletePhysicalMenuRetrieval();
                    }
                    break;
            }
        }

        private void BeginPhysicalMenuDelivery(
            bool immediate,
            bool whileBrowsing)
        {
            if (!UsesPhysicalMenu)
            {
                return;
            }

            counterMenuModel.Reset();
            counterMenuModel.BeginDelivery();
            SelectedIndex = 0;
            if (!serviceView.SelectBottle(SelectedOffer.DrinkId))
            {
                throw new InvalidOperationException(
                    "The reset bar-menu selection has no service bottle.");
            }

            FeedbackKey = string.Empty;
            counterMenuHint?.Hide();
            menuPresentation.SetSelection(SelectedIndex, false);
            menuPresentation.BeginDelivery();
            menuDeliveryWhileBrowsing = whileBrowsing && !immediate;
            menuDeliveryElapsedSeconds = 0f;
            if (immediate)
            {
                menuPresentation.EvaluateDelivery(1f);
                CompletePhysicalMenuDelivery();
            }
        }

        private void CompletePhysicalMenuDelivery()
        {
            if (!UsesPhysicalMenu ||
                counterMenuModel.State != CounterMenuState.Delivering)
            {
                return;
            }

            menuPresentation.CompleteDelivery();
            counterMenuModel.Open();
            menuDeliveryWhileBrowsing = false;
            menuDeliveryElapsedSeconds = 0f;
            menuPresentation.SetSelection(SelectedIndex, false);
            counterSeatView.BeginMenuFocus(
                menuPresentation.ResolveCameraFocusPose(
                    counterSeatView.CurrentCameraPosition),
                BarDrinkMenuPresentation.CameraFocusFieldOfView);
            inputUnlockFrame = Time.frameCount + 1;
        }

        private void CompletePhysicalMenuRetrieval()
        {
            if (!UsesPhysicalMenu ||
                counterMenuModel.State != CounterMenuState.Retrieving)
            {
                return;
            }

            menuPresentation.CompleteRetrieval();
            counterMenuModel.CompleteRetrieval();
            menuDeliveryWhileBrowsing = false;
            menuDeliveryElapsedSeconds = 0f;
            counterMenuHint?.Hide();
        }

        private void AdvanceBrowsingMenuDelivery(float unscaledDeltaTime)
        {
            if (!menuDeliveryWhileBrowsing ||
                counterMenuModel == null ||
                counterMenuModel.State != CounterMenuState.Delivering)
            {
                return;
            }

            menuDeliveryElapsedSeconds += Mathf.Max(
                0f,
                unscaledDeltaTime);
            float duration =
                BarDrinkServiceTimeline.CameraApproachDurationSeconds;
            float progress = duration > 0f
                ? Mathf.Clamp01(menuDeliveryElapsedSeconds / duration)
                : 1f;
            menuPresentation.EvaluateDelivery(progress);
            if (progress >= 1f)
            {
                CompletePhysicalMenuDelivery();
            }
        }

        private void ApplyCamera(float blend)
        {
            if (cameraFollow == null || counterSeatView != null)
            {
                return;
            }

            float amount = Mathf.Clamp01(blend);
            float remaining = 1f - amount;
            Vector3 position =
                remaining * remaining * cameraStartPosition +
                2f * remaining * amount * cameraControlPosition +
                amount * amount * cameraTargetPosition;
            cameraFollow.SetFixedPose(
                position,
                Quaternion.Slerp(
                    cameraStartRotation,
                    cameraTargetRotation,
                    amount),
                Mathf.Lerp(
                    cameraStartFieldOfView,
                    cameraTargetFieldOfView,
                    amount));
            CinematicDepthOfField.SetFocusDistance(
                Vector3.Distance(
                    position,
                    depthOfFieldFocusPoint));
        }

        private void ApplyBottlePresentation(BarDrinkServiceFrame frame)
        {
            BarDrinkBottleView bottle = serviceView.SelectedBottle;
            if (bottle == null || !timeline.IsCommitted)
            {
                return;
            }

            bool bottleIsInFlight =
                frame.Phase == BarDrinkServicePhase.BottlePickup ||
                frame.Phase == BarDrinkServicePhase.VesselPlacement ||
                frame.Phase == BarDrinkServicePhase.Pouring ||
                frame.Phase == BarDrinkServicePhase.BottleReturn;
            if (!bottleIsInFlight)
            {
                serviceView.ResetSelectedBottle();
                return;
            }

            // The bottle never flies to the hero: the bartender's arm
            // carries it from the shelf to the pour spot over the
            // counter, with a small lift arc so the pickup reads as a
            // grab rather than a slide.
            BarDrinkServicePose pourLocal = servicePlan.BottlePourPose;
            Vector3 pourPosition =
                serviceView.transform.TransformPoint(pourLocal.Position);
            Quaternion pourRotation =
                serviceView.transform.rotation * pourLocal.Rotation;
            Vector3 travelPosition = Vector3.Lerp(
                bottleStartPosition,
                pourPosition,
                frame.BottleTravel);
            travelPosition.y +=
                Mathf.Sin(Mathf.Clamp01(frame.BottleTravel) *
                          Mathf.PI) * 0.10f;
            serviceView.SetSelectedBottleWorldPose(
                travelPosition,
                Quaternion.Slerp(
                    bottleStartRotation,
                    pourRotation,
                    frame.BottleTilt));
        }

        private void ApplyVesselPresentation(BarDrinkServiceFrame frame)
        {
            BarDrinkVesselView vessel = serviceView.ActiveVessel;
            if (vessel == null)
            {
                serviceView.HidePourStream();
                return;
            }

            float visibility = Mathf.Clamp01(frame.VesselVisibility);
            bool visible = visibility > 0.002f;
            vessel.gameObject.SetActive(visible);
            if (visible)
            {
                vessel.transform.localScale = vesselBaseScale;
                if (frame.DrinkLift > 0f)
                {
                    BarDrinkServicePose counter =
                        servicePlan.VesselCounterPose;
                    Vector3 counterPosition =
                        serviceView.transform.TransformPoint(
                            counter.Position);
                    Quaternion counterRotation =
                        serviceView.transform.rotation * counter.Rotation;
                    Transform hand =
                        firstPersonArms.LeftVesselAttachmentAnchor;
                    vessel.SetWorldPose(
                        Vector3.Lerp(
                            counterPosition,
                            hand.position,
                            frame.DrinkLift),
                        Quaternion.Slerp(
                            counterRotation,
                            hand.rotation,
                            frame.DrinkLift));
                }
                else
                {
                    // The bartender slides the vessel in flat along
                    // the counter from past the left edge of the
                    // seated frame; VesselVisibility is the slide.
                    BarDrinkServicePose counter =
                        servicePlan.VesselCounterPose;
                    float slide = Mathf.SmoothStep(
                        0f,
                        1f,
                        visibility);
                    serviceView.SetActiveVesselLocalPose(
                        new BarDrinkServicePose(
                            Vector3.Lerp(
                                counter.Position +
                                VesselSlideEntryOffset,
                                counter.Position,
                                slide),
                            counter.Rotation));
                }

                serviceView.SetFillProgress(frame.VesselFill);
            }

            if (frame.StreamVisibility > 0.002f)
            {
                Color streamColor = activePresentation.LiquidColor;
                streamColor.a = frame.StreamVisibility;
                serviceView.SetPourStreamFromBottle(
                    streamColor,
                    Mathf.Lerp(
                        0.006f,
                        0.019f,
                        frame.StreamVisibility));
            }
            else
            {
                serviceView.HidePourStream();
            }
        }

        private void CapturePlayerVisualState()
        {
            playerVisualHideLease?.Dispose();
            playerVisualHideLease = null;
            playerVisualStateCaptured = true;
            playerVisualHidden = false;
        }

        private void ApplyPlayerVisualForFrame(
            BarDrinkServiceFrame frame)
        {
            if (counterSeatView != null)
            {
                return;
            }

            bool shouldHide =
                frame.Phase != BarDrinkServicePhase.CameraReturn &&
                frame.Phase != BarDrinkServicePhase.Closed &&
                firstPersonArms.IsVisible;
            SetPlayerVisualHidden(shouldHide);
        }

        private void SetPlayerVisualHidden(bool hidden)
        {
            if (!playerVisualStateCaptured || playerVisualHidden == hidden)
            {
                return;
            }

            if (hidden)
            {
                playerVisualHideLease =
                    player.PresentationVisibility?.AcquireHidden(this);
            }
            else
            {
                playerVisualHideLease?.Dispose();
                playerVisualHideLease = null;
            }

            playerVisualHidden = hidden;
        }

        private void RestorePlayerVisual()
        {
            if (!playerVisualStateCaptured)
            {
                return;
            }

            SetPlayerVisualHidden(false);
            playerVisualHideLease?.Dispose();
            playerVisualHideLease = null;
            playerVisualStateCaptured = false;
            playerVisualHidden = false;
        }

        private void CaptureAndHideSceneMarkers()
        {
            if (sceneMarkerStateCaptured || sceneMarkerRenderers.Length == 0)
            {
                return;
            }

            for (int index = 0;
                 index < sceneMarkerRenderers.Length;
                 index++)
            {
                Renderer marker = sceneMarkerRenderers[index];
                previousSceneMarkerStates[index] =
                    marker != null && marker.enabled;
                if (marker != null)
                {
                    marker.enabled = false;
                }
            }

            sceneMarkerStateCaptured = true;
        }

        private void RestoreSceneMarkers()
        {
            if (!sceneMarkerStateCaptured)
            {
                return;
            }

            for (int index = 0;
                 index < sceneMarkerRenderers.Length;
                 index++)
            {
                Renderer marker = sceneMarkerRenderers[index];
                if (marker != null)
                {
                    marker.enabled = previousSceneMarkerStates[index];
                }
            }

            sceneMarkerStateCaptured = false;
        }

        private void RestoreOwnedState()
        {
            hoveredBottle = null;
            counterSeatView?.EndMenuFocus();
            counterMenuHint?.Hide();
            counterMenuModel?.Reset();
            menuDeliveryWhileBrowsing = false;
            menuDeliveryElapsedSeconds = 0f;
            menuPresentation?.ResetPresentation();
            serviceView?.ResetPresentation();
            firstPersonArms?.Hide();
            RestorePlayerVisual();
            RestoreCameraState();
            RestoreSceneMarkers();
            modalLock.Restore();
            purchaseCommitted = false;
            ResetCueState();
            Physics.SyncTransforms();
        }

        private void CompleteOrderPresentation()
        {
            hoveredBottle = null;
            serviceView.HidePourStream();
            serviceView.HideVessel();
            if (serviceView.SelectedBottle != null)
            {
                serviceView.ResetSelectedBottle();
            }
            else
            {
                serviceView.SelectBottle(SelectedOffer.DrinkId);
            }

            purchaseCommitted = false;
            FeedbackKey = string.Empty;
            inputUnlockFrame = Time.frameCount + 1;
            ResetCueState();
            if (UsesPhysicalMenu)
            {
                if (counterMenuModel.State ==
                    CounterMenuState.Retrieving)
                {
                    CompletePhysicalMenuRetrieval();
                }

                BeginPhysicalMenuDelivery(
                    immediate: false,
                    whileBrowsing: true);
            }

            Physics.SyncTransforms();
        }

        private void RestoreCameraState()
        {
            CinematicDepthOfField.End();
            if (cameraFollow == null ||
                !hasPhysicalPresentation ||
                counterSeatView != null)
            {
                return;
            }

            if (cameraWasFixed)
            {
                cameraFollow.SetFixedPose(
                    previousFixedPosition,
                    previousFixedRotation,
                    previousFixedFieldOfView);
                return;
            }

            cameraFollow.ClearFixedPose();
            cameraFollow.Snap();
        }

        private void ResetCueState()
        {
            clinkPlayed = false;
            pourPlayed = false;
            gulpPlayed = false;
        }

        private void PlayCrossedCues()
        {
            if (!timeline.IsCommitted)
            {
                return;
            }

            BarDrinkServicePhase phase = timeline.Phase;
            if (!clinkPlayed &&
                phase >= BarDrinkServicePhase.VesselPlacement)
            {
                clinkPlayed = true;
                RetroAudio.Play(RetroSfxId.Clink);
            }

            if (!pourPlayed && phase >= BarDrinkServicePhase.Pouring)
            {
                pourPlayed = true;
                RetroAudio.Play(RetroSfxId.Pour);
            }

            if (!gulpPlayed && phase >= BarDrinkServicePhase.Drinking)
            {
                gulpPlayed = true;
                RetroAudio.Play(RetroSfxId.DrinkGulp);
            }
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
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.enterKey.wasPressedThisFrame ||
                 keyboard.numpadEnterKey.wasPressedThisFrame ||
                 keyboard.eKey.wasPressedThisFrame))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonSouth.wasPressedThisFrame;
        }

        private static bool IsCancelPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                keyboard.escapeKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonEast.wasPressedThisFrame;
        }

        private static string GetFailureKey(
            DrinkPurchaseStatus status)
        {
            switch (status)
            {
                case DrinkPurchaseStatus.InsufficientFunds:
                    return "drink_shop.failure.insufficient_funds";
                case DrinkPurchaseStatus.MaximumIntoxication:
                    return "drink_shop.failure.maximum_intoxication";
                case DrinkPurchaseStatus.NotOffered:
                default:
                    return "drink_shop.failure.not_offered";
            }
        }
    }
}
