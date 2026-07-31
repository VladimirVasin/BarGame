using System;
using System.Collections.Generic;
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
        private readonly bool[] previousRigRendererStates =
            new bool[PlayerSpriteRig.PartCount];
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
        private Camera targetCamera;
        private BarDrinkBottleView hoveredBottle;
        private BarDrinkPresentation activePresentation;
        private Vector3 cameraStartPosition;
        private Quaternion cameraStartRotation;
        private float cameraStartFieldOfView;
        private Vector3 cameraControlPosition;
        private Vector3 cameraTargetPosition;
        private Quaternion cameraTargetRotation;
        private float cameraTargetFieldOfView;
        private bool cameraWasFixed;
        private Vector3 previousFixedPosition;
        private Quaternion previousFixedRotation;
        private float previousFixedFieldOfView;
        private Vector3 bottleStartPosition;
        private Quaternion bottleStartRotation;
        private Vector3 vesselBaseScale = Vector3.one;
        private int inputUnlockFrame;
        private bool hasPhysicalPresentation;
        private bool playerVisualStateCaptured;
        private bool playerVisualHidden;
        private bool sceneMarkerStateCaptured;
        private bool previousDynamicShadowEnabled;
        private bool previousContactShadowEnabled;
        private bool purchaseCommitted;
        private bool clinkPlayed;
        private bool pourPlayed;
        private bool gulpPlayed;

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
            (!hasPhysicalPresentation || timeline.IsBrowsing);
        public bool IsServing =>
            IsOpen && hasPhysicalPresentation && timeline.IsCommitted;
        public bool PurchaseCommitted => purchaseCommitted;
        public BarDrinkServiceTimeline Timeline => timeline;
        public BarDrinkServiceView ServiceView => serviceView;
        public BarDrinkFirstPersonArms FirstPersonArms => firstPersonArms;
        public BarDrinkBottleView HoveredBottle => hoveredBottle;

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
            sceneMarkerRenderers = Array.Empty<Renderer>();
            previousSceneMarkerStates = Array.Empty<bool>();
            sceneMarkerStateCaptured = false;
            timeline = new BarDrinkServiceTimeline();
            view?.Initialize(this);
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
                CaptureAndHideSceneMarkers();
                SelectedIndex = 0;
                FeedbackKey = string.Empty;
                inputUnlockFrame = Time.frameCount + 1;
                purchaseCommitted = false;
                ResetCueState();
                if (hasPhysicalPresentation)
                {
                    CaptureCameraPath();
                    CapturePlayerVisualState();
                    serviceView.ResetPresentation();
                    serviceView.SelectBottle(SelectedOffer.DrinkId);
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
            SelectedIndex = index;
            FeedbackKey = string.Empty;
            if (hasPhysicalPresentation)
            {
                serviceView.SelectBottle(SelectedOffer.DrinkId);
            }

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
            if (!IsBrowsing || Offers.Count == 0)
            {
                return false;
            }

            DrinkPurchaseResult result =
                GameSessionState.TryPurchaseDrink(SelectedOffer.DrinkId);
            if (!result.Succeeded)
            {
                FeedbackKey = GetFailureKey(result.Status);
                RetroAudio.Play(RetroSfxId.Bad);
                return false;
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
        }

        public void AdvancePresentation(float unscaledDeltaTime)
        {
            if (!IsOpen || !hasPhysicalPresentation)
            {
                return;
            }

            bool wasCommitted = timeline.IsCommitted;
            timeline.Advance(unscaledDeltaTime);
            PlayCrossedCues();
            if (wasCommitted &&
                !timeline.IsCommitted &&
                timeline.IsBrowsing)
            {
                CompleteOrderPresentation();
            }

            ApplyCurrentPresentation();
            if (timeline.Phase == BarDrinkServicePhase.Closed)
            {
                IsOpen = false;
                RestoreOwnedState();
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
                if (!timeline.IsCommitted &&
                    !timeline.IsBrowsing &&
                    Time.frameCount > inputUnlockFrame &&
                    IsCancelPressed())
                {
                    Cancel();
                }

                if (timeline.IsBrowsing &&
                    Time.frameCount > inputUnlockFrame)
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
        }

        private void HandleBrowsingInput()
        {
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
        }

        private void ApplyCurrentPresentation()
        {
            if (!IsOpen || !hasPhysicalPresentation || timeline == null)
            {
                return;
            }

            BarDrinkServiceFrame frame = timeline.CurrentFrame;
            ApplyCamera(frame.CameraBlend);
            float rightGrip =
                Mathf.Clamp01(frame.BottleTravel + frame.BottleTilt);
            firstPersonArms.ApplyPresentation(
                frame.ArmsVisibility,
                rightGrip,
                frame.DrinkLift);
            ApplyPlayerVisualForFrame(frame);
            ApplyBottlePresentation(frame);
            ApplyVesselPresentation(frame);
        }

        private void ApplyCamera(float blend)
        {
            if (cameraFollow == null)
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

            Transform grip = firstPersonArms.RightBottleGripAnchor;
            Vector3 handPosition = grip.position;
            Quaternion handRotation = grip.rotation;
            BarDrinkServicePose pourLocal = servicePlan.BottlePourPose;
            Vector3 pourPosition =
                serviceView.transform.TransformPoint(pourLocal.Position);
            Quaternion pourRotation =
                serviceView.transform.rotation * pourLocal.Rotation;
            Vector3 targetPosition = Vector3.Lerp(
                handPosition,
                pourPosition,
                frame.BottleTilt);
            Quaternion targetRotation = Quaternion.Slerp(
                handRotation,
                pourRotation,
                frame.BottleTilt);
            serviceView.SetSelectedBottleWorldPose(
                Vector3.Lerp(
                    bottleStartPosition,
                    targetPosition,
                    frame.BottleTravel),
                Quaternion.Slerp(
                    bottleStartRotation,
                    targetRotation,
                    frame.BottleTravel));
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
                vessel.transform.localScale =
                    vesselBaseScale * Mathf.Max(0.02f, visibility);
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
                    serviceView.SetActiveVesselAtCounter();
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
            previousDynamicShadowEnabled =
                player.Shadow != null && player.Shadow.enabled;
            previousContactShadowEnabled =
                player.ContactShadow != null &&
                player.ContactShadow.enabled;
            IReadOnlyList<SpriteRenderer> renderers =
                player.Visual.Renderers;
            for (int index = 0;
                 index < PlayerSpriteRig.PartCount;
                 index++)
            {
                SpriteRenderer renderer =
                    index < renderers.Count ? renderers[index] : null;
                previousRigRendererStates[index] =
                    renderer != null && renderer.enabled;
            }

            playerVisualStateCaptured = true;
            playerVisualHidden = false;
        }

        private void ApplyPlayerVisualForFrame(
            BarDrinkServiceFrame frame)
        {
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

            IReadOnlyList<SpriteRenderer> renderers =
                player.Visual.Renderers;
            for (int index = 0;
                 index < PlayerSpriteRig.PartCount;
                 index++)
            {
                SpriteRenderer renderer =
                    index < renderers.Count ? renderers[index] : null;
                if (renderer != null)
                {
                    renderer.enabled =
                        hidden ? false : previousRigRendererStates[index];
                }
            }

            if (player.Shadow != null)
            {
                player.Shadow.enabled =
                    hidden ? false : previousDynamicShadowEnabled;
            }

            if (player.ContactShadow != null)
            {
                player.ContactShadow.enabled =
                    hidden ? false : previousContactShadowEnabled;
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
            Physics.SyncTransforms();
        }

        private void RestoreCameraState()
        {
            if (cameraFollow == null || !hasPhysicalPresentation)
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
