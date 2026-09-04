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
    /// modal seated counter menu and physical serving presentation.
    /// The legacy Initialize overload is retained for isolated modal callers;
    /// production bars always supply a generated BarDrinkServiceView.
    /// </summary>
    [DefaultExecutionOrder(260)]
    [DisallowMultipleComponent]
    public sealed class BarDrinkShopController : MonoBehaviour
    {
        private const int MaximumRayHits = 48;
        public const string PhysicalMenuOrderHintKey =
            "bar.menu.order_hint";
        public const float BottleGripReachMargin = 0.03f;
        public const float CounterMenuDepthOfFieldAperture = 8f;
        public const float CounterMenuDepthOfFieldFocalLength = 35f;

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
        private PlayerAnimatedInteractionController playerInteraction;
        private Transform playerDrinkUpperArm;
        private Transform playerDrinkForearm;
        private Transform playerDrinkHand;
        private Transform playerDrinkRightGrip;
        private Transform playerDrinkMouth;
        private Transform playerDrinkOwnerRoot;
        private SeatedArmHandAttachment playerDrinkHandAttachment;
        private bool playerDrinkRigConfigured;
        private CounterSeatView counterSeatView;
        private BarDrinkMenuPresentation menuPresentation;
        private CounterMenuModel counterMenuModel;
        private CounterMenuHintView counterMenuHint;
        private Transform bottleCarrier;
        private Transform bottleHolderRoot;
        private Transform bottleReachShoulder;
        private Transform bottleReachElbow;
        private Transform bottleReachWrist;
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
        private Vector3 bottleShelfReturnPosition;
        private Quaternion bottleShelfReturnRotation = Quaternion.identity;
        private Vector3 vesselBaseScale = Vector3.one;
        private Vector3 activeBottleHandTarget;
        private Vector3 activeBottleReachCorrection;
        private Vector3 activeBottleHandRadial = Vector3.right;
        private Quaternion activeBottleWorldRotation = Quaternion.identity;
        private Vector3 beerPlacementStartPosition;
        private Quaternion beerPlacementStartRotation = Quaternion.identity;
        private bool beerPlacementCaptured;
        private bool bottleGripAttached;
        private bool bottleReturnCaptured;
        private Vector3 bottleReturnHandStart;
        private Vector3 activeServiceLocalOffset;
        private bool activeServiceMirrored;

        /// <summary>
        /// How far the vessel enters from the server's side of the chosen
        /// stool, in service-plan local space. Mirrored stations reverse it;
        /// this remains a short flat slide, never a spawn at the guest.
        /// </summary>
        public const float VesselSlideEntryDistance = 0.50f;
        public const float BeerGuestServerSetback = 1.02f;
        public const float BeerVesselServerwardOffset = 0.18f;
        public const float BottleShelfServerSetback = 0.42f;
        public const float BottlePreparationServerSetback = 0.55f;
        public const float BottleGripAttachProgress = 0.48f;
        public const float BottlePourMouthGap = 0.10f;
        private int inputUnlockFrame;
        private bool hasPhysicalPresentation;
        private bool playerVisualStateCaptured;
        private bool playerVisualHidden;
        private IDisposable playerVisualHideLease;
        private bool sceneMarkerStateCaptured;
        private bool purchaseCommitted;
        private DrinkOrderToken pendingOrder;
        private bool drinkActionPending;
        private int playerDrinkStartedFrame = -1;
        private bool clinkPlayed;
        private bool pourPlayed;
        private bool gulpPlayed;
        private float menuDeliveryElapsedSeconds;
        private bool menuPlacementStarted;
        private bool counterServerAtTarget;
        private bool returningCounterMenuHome;
        private bool restingMenuGazeArmed;

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
            IsOpen && hasPhysicalPresentation &&
            (timeline.IsCommitted || drinkActionPending);
        public bool CanDrinkServedVessel =>
            IsOpen && UsesPhysicalMenu && timeline != null &&
            timeline.CanBeginDrink && serviceView?.ActiveVessel != null;
        public bool IsLookingAtServedVessel =>
            CanDrinkServedVessel && targetCamera != null &&
            !CounterMenuInput.IsBlockedByOtherUi() &&
            serviceView.ActiveVessel.IsLookingAt(targetCamera);
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
        public bool HasPhysicalMenuPresentation =>
            menuPresentation != null && counterMenuModel != null;
        public bool UsesPhysicalMenu => counterSeatView != null &&
            HasPhysicalMenuPresentation;
        public bool CanExitPhysicalMenu => UsesPhysicalMenu && IsOpen &&
            !IsServing && timeline != null && timeline.CanCancel;
        public bool CanRestPhysicalMenu => UsesPhysicalMenu && IsOpen &&
            !IsServing && timeline != null && timeline.IsBrowsing &&
            counterMenuModel.State == CounterMenuState.Open;
        public bool CanStandAfterMenuRested => UsesPhysicalMenu && IsOpen &&
            !IsServing && timeline != null && timeline.IsBrowsing &&
            counterMenuModel.State == CounterMenuState.Resting;
        public bool IsLookingAtRestingMenu => UsesPhysicalMenu &&
            counterMenuModel.State == CounterMenuState.Resting &&
            restingMenuGazeArmed &&
            menuPresentation.IsLookingAtRestingMenu(targetCamera);
        public bool HasCounterServiceTarget => counterSeatView != null &&
            IsOpen && !returningCounterMenuHome;
        public Vector3 ActiveServiceLocalOffset => activeServiceLocalOffset;
        public bool ActiveServiceMirrored => activeServiceMirrored;
        public bool IsReturningCounterMenuHome =>
            returningCounterMenuHome;
        public Vector3 ActiveBottleHandTarget => activeBottleHandTarget;
        public Vector3 ActiveBottleReachCorrection =>
            activeBottleReachCorrection;
        public Quaternion ActiveBottleWorldRotation =>
            activeBottleWorldRotation;
        public Vector3 ActiveBottleHandRadial => activeBottleHandRadial;
        public bool IsBottleGripAttached => bottleGripAttached;
        public float BottleGripError =>
            serviceView != null &&
            serviceView.IsCarriedBottleVisible &&
            bottleCarrier != null
                ? Vector3.Distance(
                    serviceView.ResolveCarriedBottleHandContact(),
                    bottleCarrier.position)
                : float.PositiveInfinity;
        public float BottleHandRadialClearance =>
            serviceView != null
                ? serviceView.CarriedBottleHandRadialClearance
                : 0f;
        public float BottleGripReachLimit => ResolveBottleGripReachLimit();
        public float PlayerVesselGripError =>
            serviceView != null && playerDrinkRightGrip != null
                ? serviceView.ResolveActiveVesselGripError(
                    playerDrinkRightGrip)
                : float.PositiveInfinity;
        public float PlayerVesselDrinkRimError =>
            serviceView?.ActiveVessel != null && playerDrinkMouth != null
                ? serviceView.ActiveVessel.ResolveDrinkRimError(
                    playerDrinkMouth)
                : float.PositiveInfinity;
        public float PlayerVesselHorizontalErrorDegrees =>
            serviceView?.ActiveVessel != null &&
            playerDrinkOwnerRoot != null
                ? Mathf.Abs(
                    90f - Vector3.Angle(
                        playerDrinkOwnerRoot.up,
                        serviceView.ActiveVessel.OpeningDirection))
                : float.PositiveInfinity;
        public float PlayerVesselHandleRightAlignment
        {
            get
            {
                BarDrinkVesselView vessel = serviceView?.ActiveVessel;
                if (vessel == null || playerDrinkOwnerRoot == null)
                {
                    return -1f;
                }

                Vector3 opening = vessel.OpeningDirection;
                Vector3 handle = Vector3.ProjectOnPlane(
                    vessel.HandleDirection,
                    opening);
                Vector3 right = Vector3.ProjectOnPlane(
                    playerDrinkOwnerRoot.right,
                    opening);
                return handle.sqrMagnitude > 0.000001f &&
                       right.sqrMagnitude > 0.000001f
                    ? Vector3.Dot(handle.normalized, right.normalized)
                    : -1f;
            }
        }

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
            playerInteraction = null;
            ResetPlayerDrinkRig();
            counterSeatView = null;
            menuPresentation = null;
            counterMenuModel = null;
            activeServiceLocalOffset = Vector3.zero;
            activeServiceMirrored = false;
            menuPlacementStarted = false;
            counterServerAtTarget = false;
            returningCounterMenuHome = false;
            restingMenuGazeArmed = false;
            counterMenuHint?.Hide();
            counterMenuHint = null;
            bottleCarrier = null;
            bottleHolderRoot = null;
            bottleReachShoulder = null;
            bottleReachElbow = null;
            bottleReachWrist = null;
            activeBottleHandTarget = Vector3.zero;
            activeBottleReachCorrection = Vector3.zero;
            activeBottleHandRadial = Vector3.right;
            activeBottleWorldRotation = Quaternion.identity;
            beerPlacementStartPosition = Vector3.zero;
            beerPlacementStartRotation = Quaternion.identity;
            beerPlacementCaptured = false;
            bottleGripAttached = false;
            bottleReturnCaptured = false;
            bottleReturnHandStart = Vector3.zero;
            bottleShelfReturnPosition = Vector3.zero;
            bottleShelfReturnRotation = Quaternion.identity;
            sceneMarkerRenderers = Array.Empty<Renderer>();
            previousSceneMarkerStates = Array.Empty<bool>();
            sceneMarkerStateCaptured = false;
            timeline = new BarDrinkServiceTimeline();
            view?.Initialize(this);
        }

        /// <summary>
        /// Hands camera and world-hero visibility ownership to a physical
        /// counter seat. The shop continues to own the transaction, modal UI,
        /// non-beer attachment rig and drink-service timeline.
        /// </summary>
        public void ConfigureSeatedView(CounterSeatView seatedView)
        {
            ConfigureSeatedView(seatedView, Vector3.zero, false);
        }

        public void ConfigureSeatedView(
            CounterSeatView seatedView,
            Vector3 serviceLocalOffset,
            bool mirrorServiceHorizontally = false)
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
            activeServiceLocalOffset = serviceLocalOffset;
            activeServiceMirrored = mirrorServiceHorizontally;
            menuPlacementStarted = false;
            counterServerAtTarget = false;
            returningCounterMenuHome = false;
            restingMenuGazeArmed = false;
            if (menuPresentation == null || !menuPresentation.IsConfigured ||
                counterMenuModel == null)
            {
                throw new InvalidOperationException(
                    "A seated bar requires the authored physical menu.");
            }

            Vector3 menuDockOffset = activeServiceLocalOffset;
            if (activeServiceMirrored)
            {
                menuDockOffset.x += 2f *
                    (servicePlan.SeatPose.Position.x -
                     servicePlan.MenuPose.Position.x);
            }

            menuPresentation.ConfigureDockOffset(menuDockOffset);
            if (counterMenuHint == null)
            {
                counterMenuHint = CounterMenuHintView.Create(
                    transform,
                    "Bar Drink Menu Hint",
                    MountainRoadCafeMenuHintView.SelectHintKey,
                    PhysicalMenuOrderHintKey);
            }
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

        public void ConfigureBottleCarrier(Transform carrier)
        {
            if (!HasPhysicalMenuPresentation)
            {
                throw new InvalidOperationException(
                    "The physical bar service is not initialized.");
            }

            bottleCarrier = carrier != null
                ? carrier
                : throw new ArgumentNullException(nameof(carrier));
        }

        /// <summary>
        /// Supplies the ordinary bartender's anatomical right-arm chain. The
        /// service pose remains mirrored per stool, but its final hand target
        /// is clamped to this physical chain before IK sees it.
        /// </summary>
        public void ConfigureBottleReachChain(
            Transform holderRoot,
            Transform shoulder,
            Transform elbow,
            Transform wrist,
            Transform grip)
        {
            bottleHolderRoot = holderRoot != null
                ? holderRoot
                : throw new ArgumentNullException(nameof(holderRoot));
            bottleReachShoulder = shoulder != null
                ? shoulder
                : throw new ArgumentNullException(nameof(shoulder));
            bottleReachElbow = elbow != null
                ? elbow
                : throw new ArgumentNullException(nameof(elbow));
            bottleReachWrist = wrist != null
                ? wrist
                : throw new ArgumentNullException(nameof(wrist));
            Transform configuredGrip = grip != null
                ? grip
                : throw new ArgumentNullException(nameof(grip));
            if (bottleCarrier != null && bottleCarrier != configuredGrip)
            {
                throw new ArgumentException(
                    "The reach chain grip must match the bottle carrier.",
                    nameof(grip));
            }

            bottleCarrier = configuredGrip;
        }

        /// <summary>
        /// Releases a seat that reserved the shared shop but never reached
        /// its seated loop. The identity check keeps a stale station from
        /// clearing a newer station's binding.
        /// </summary>
        public bool TryReleaseSeatedView(CounterSeatView expectedView)
        {
            if (IsOpen || expectedView == null ||
                !ReferenceEquals(counterSeatView, expectedView))
            {
                return false;
            }

            counterSeatView = null;
            activeServiceLocalOffset = Vector3.zero;
            activeServiceMirrored = false;
            menuPlacementStarted = false;
            counterServerAtTarget = false;
            returningCounterMenuHome = false;
            restingMenuGazeArmed = false;
            if (menuPresentation != null && menuPresentation.IsConfigured)
            {
                menuPresentation.ConfigureDockOffset(Vector3.zero);
            }

            return true;
        }

        /// <summary>
        /// Drops the bar-owned close-up effect before the seat view restores
        /// the chase camera. The identity guard prevents a stale station from
        /// releasing a newer seated session's presentation.
        /// </summary>
        public bool TryReleaseSeatedCameraEffects(
            CounterSeatView expectedView)
        {
            if (!IsOpen || expectedView == null ||
                !ReferenceEquals(counterSeatView, expectedView))
            {
                return false;
            }

            CinematicDepthOfField.EndImmediately();
            return true;
        }

        public Vector3 ResolveActiveServiceLocalPosition(
            Vector3 canonicalLocalPosition)
        {
            Vector3 positioned =
                canonicalLocalPosition + activeServiceLocalOffset;
            if (activeServiceMirrored && servicePlan != null)
            {
                float activeSeatX =
                    servicePlan.SeatPose.Position.x +
                    activeServiceLocalOffset.x;
                positioned.x = activeSeatX * 2f - positioned.x;
            }

            return positioned;
        }

        public BarDrinkServicePose ResolveActiveServiceLocalPose(
            BarDrinkServicePose canonicalLocalPose)
        {
            Quaternion rotation = canonicalLocalPose.Rotation;
            if (activeServiceMirrored)
            {
                Vector3 forward = rotation * Vector3.forward;
                Vector3 up = rotation * Vector3.up;
                forward.x = -forward.x;
                up.x = -up.x;
                rotation = Quaternion.LookRotation(forward, up);
            }

            return new BarDrinkServicePose(
                ResolveActiveServiceLocalPosition(
                    canonicalLocalPose.Position),
                rotation);
        }

        /// <summary>
        /// Brings the beer server to the inner counter lip before placement.
        /// The ordinary home line is too far from the guest-side mug pose for
        /// a human arm, which previously made the final movement read as a
        /// throw across the bar.
        /// </summary>
        public Vector3 ResolveBeerGuestServerLocalPosition(
            Vector3 canonicalLocalPosition)
        {
            Vector3 positioned = ResolveActiveServiceLocalPosition(
                canonicalLocalPosition);
            if (servicePlan == null)
            {
                return positioned;
            }

            BarDrinkServicePose counter = ResolveActiveServiceLocalPose(
                servicePlan.VesselCounterPose);
            positioned.x = counter.Position.x;
            positioned.z = Mathf.Min(
                positioned.z,
                counter.Position.z + BeerGuestServerSetback);
            return positioned;
        }

        public Pose ResolveBottleShelfServerWorldPose()
        {
            if (serviceView == null || serviceView.SelectedBottle == null)
            {
                return new Pose(transform.position, transform.rotation);
            }

            bool returningBottle = timeline != null &&
                (timeline.Phase ==
                     BarDrinkServicePhase.BottleWalkToShelfReturn ||
                 timeline.Phase == BarDrinkServicePhase.BottleReturn);
            Vector3 bottleWorld = returningBottle
                ? bottleShelfReturnPosition
                : serviceView.SelectedBottle.transform.position;
            Vector3 bottleLocal = serviceView.transform.InverseTransformPoint(
                bottleWorld);
            Vector3 serverLocal = new Vector3(
                bottleLocal.x,
                0f,
                bottleLocal.z - BottleShelfServerSetback);
            return new Pose(
                serviceView.transform.TransformPoint(serverLocal),
                serviceView.transform.rotation);
        }

        public Pose ResolveBottlePreparationServerWorldPose()
        {
            if (serviceView == null || servicePlan == null)
            {
                return new Pose(transform.position, transform.rotation);
            }

            Vector3 vesselLocal =
                ResolveBottlePreparationLocalPosition();
            Vector3 serverLocal = new Vector3(
                vesselLocal.x,
                0f,
                vesselLocal.z + BottlePreparationServerSetback);
            return new Pose(
                serviceView.transform.TransformPoint(serverLocal),
                serviceView.transform.rotation *
                Quaternion.Euler(0f, 180f, 0f));
        }

        internal Pose ResolveBottlePreparationVesselWorldPose()
        {
            BarDrinkServicePose prep =
                servicePlan.BottlePreparationVesselPose;
            return new Pose(
                serviceView.transform.TransformPoint(
                    ResolveBottlePreparationLocalPosition()),
                serviceView.transform.rotation * prep.Rotation);
        }

        internal Pose ResolveBottlePourWorldPose()
        {
            BarDrinkServicePose pour = servicePlan.BottlePourPose;
            Quaternion rotation =
                serviceView.transform.rotation * pour.Rotation;
            return new Pose(
                ResolveBottlePourRootWorldPosition(rotation),
                rotation);
        }

        private Vector3 ResolveBottlePourRootWorldPosition(
            Quaternion bottleWorldRotation)
        {
            BarDrinkVesselView vessel = serviceView?.ActiveVessel;
            if (vessel != null && serviceView.IsCarriedBottleVisible)
            {
                Vector3 desiredMouthPosition =
                    vessel.PourTargetWorldPosition +
                    serviceView.transform.up * BottlePourMouthGap;
                return serviceView
                    .ResolveCarriedBottleRootPositionForMouth(
                        desiredMouthPosition,
                        bottleWorldRotation);
            }

            BarDrinkServicePose prep =
                servicePlan.BottlePreparationVesselPose;
            BarDrinkServicePose pour = servicePlan.BottlePourPose;
            Vector3 localPosition = pour.Position;
            localPosition.x +=
                ResolveBottlePreparationLocalPosition().x -
                prep.Position.x;
            return serviceView.transform.TransformPoint(localPosition);
        }

        private Vector3 ResolveBottlePreparationLocalPosition()
        {
            Vector3 localPosition =
                servicePlan.BottlePreparationVesselPose.Position;
            if (serviceView?.SelectedBottle != null)
            {
                localPosition.x = serviceView.transform
                    .InverseTransformPoint(
                        serviceView.SelectedBottle.transform.position).x;
            }

            return localPosition;
        }

        /// <summary>
        /// The bartender choreography reports physical arrival independently
        /// of the drink-service clock. Until then the closed booklet stays in
        /// the moving hand instead of flying to a distant stool.
        /// </summary>
        public void ReportCounterServerAtTarget(bool atTarget)
        {
            counterServerAtTarget = UsesPhysicalMenu &&
                !returningCounterMenuHome && atTarget;
        }

        public bool ReportBeerServerAtTap(bool atTarget)
        {
            return timeline != null &&
                   timeline.ReportBeerServerAtTap(atTarget);
        }

        public bool ReportBeerServerAtGuest(bool atTarget)
        {
            return timeline != null &&
                   timeline.ReportBeerServerAtGuest(atTarget);
        }

        public bool ReportBottleServerAtShelf(bool atTarget)
        {
            if (timeline == null || !atTarget ||
                timeline.Phase !=
                    BarDrinkServicePhase.BottleWalkToShelf ||
                !timeline.ReportBottleServerAtShelf(true))
            {
                return false;
            }

            BarDrinkBottleView bottle = serviceView?.SelectedBottle;
            if (bottle == null ||
                !serviceView.ShowCarriedBottle(
                    activePresentation,
                    null))
            {
                throw new InvalidOperationException(
                    "The selected shelf bottle could not enter the " +
                    "bartender's hand.");
            }

            serviceView.SetCarriedBottleWorldPose(
                bottleStartPosition,
                bottleStartRotation,
                ResolveBottleHolderPosition());
            activeBottleHandTarget =
                serviceView.ResolveCarriedBottleHandContact();
            activeBottleReachCorrection = Vector3.zero;
            activeBottleHandRadial =
                (serviceView.CarriedBottleHandContactWorldPosition -
                 serviceView.CarriedBottleGripCenterWorldPosition)
                .normalized;
            activeBottleWorldRotation = bottleStartRotation;
            bottleGripAttached = false;
            bottleReturnCaptured = false;
            return true;
        }

        public bool ReportBottleServerAtPour(bool atTarget)
        {
            return timeline != null &&
                   timeline.ReportBottleServerAtPour(atTarget);
        }

        public bool ReportBottleServerAtGuest(bool atTarget)
        {
            return timeline != null &&
                   timeline.ReportBottleServerAtGuest(atTarget);
        }

        public bool ReportBottleServerAtShelfReturn(bool atTarget)
        {
            if (timeline == null ||
                !timeline.ReportBottleServerAtShelfReturn(atTarget))
            {
                return false;
            }

            bottleReturnCaptured = false;
            ApplyBottleShelfReturn(0f);
            return true;
        }

        internal void RefreshBottlePourStreamAfterGrip()
        {
            if (timeline == null || !timeline.IsBottleService ||
                timeline.Phase != BarDrinkServicePhase.Pouring ||
                serviceView == null)
            {
                return;
            }

            float visibility = timeline.CurrentFrame.StreamVisibility;
            if (visibility <= 0.002f)
            {
                serviceView.HidePourStream();
                return;
            }

            Color streamColor = activePresentation.LiquidColor;
            streamColor.a = visibility;
            serviceView.SetPourStreamFromCarriedBottle(
                streamColor,
                Mathf.Lerp(0.006f, 0.019f, visibility));
        }

        /// <summary>
        /// Completes a seated shop only after the retrieved booklet and its
        /// carrier have visibly returned to the authored home station.
        /// </summary>
        public bool CompleteCounterMenuReturnHome()
        {
            if (!returningCounterMenuHome)
            {
                return false;
            }

            Close();
            return true;
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
            playerInteraction = playerRuntime.GameObject.GetComponent<
                PlayerAnimatedInteractionController>();
            if (playerInteraction == null ||
                !playerInteraction.IsInitialized)
            {
                throw new InvalidOperationException(
                    "Physical drink service requires the shared player " +
                    "animated-interaction controller.");
            }
            ConfigurePlayerDrinkRig(playerRuntime);
            serviceView.ResetPresentation();
            hasPhysicalPresentation = true;
        }

        private void ConfigurePlayerDrinkRig(PlayerRuntime playerRuntime)
        {
            ResetPlayerDrinkRig();
            if (!(playerRuntime.Visual is
                    Player3DCharacterPresentation presentation) ||
                presentation.Registry == null)
            {
                throw new InvalidOperationException(
                    "Physical bar drinking requires the production 3D " +
                    "player registry.");
            }

            Player3DAssetRegistry registry = presentation.Registry;
            playerDrinkUpperArm = RequirePlayerDrinkBone(
                registry,
                Player3DAnatomicalPart.RightUpperArm);
            playerDrinkForearm = RequirePlayerDrinkBone(
                registry,
                Player3DAnatomicalPart.RightForearm);
            playerDrinkHand = RequirePlayerDrinkBone(
                registry,
                Player3DAnatomicalPart.RightHand);
            playerDrinkRightGrip =
                playerInteraction.RightVesselGripAnchor;
            playerDrinkMouth = playerInteraction.MouthAnchor;
            playerDrinkOwnerRoot = playerRuntime.GameObject.transform;
            if (playerDrinkRightGrip == null || playerDrinkMouth == null)
            {
                throw new InvalidOperationException(
                    "Physical bar drinking requires the Hero V2 right-grip " +
                    "and mouth sockets.");
            }

            playerDrinkHandAttachment = new SeatedArmHandAttachment(
                playerDrinkHand,
                playerDrinkRightGrip);
            playerDrinkRigConfigured = true;
        }

        private static Transform RequirePlayerDrinkBone(
            Player3DAssetRegistry registry,
            Player3DAnatomicalPart part)
        {
            if (!registry.TryGetPart(
                    part,
                    out Player3DAnatomicalPartBinding binding) ||
                binding?.Bone == null)
            {
                throw new InvalidOperationException(
                    $"Physical bar drinking requires the registered " +
                    $"{part} bone.");
            }

            return binding.Bone;
        }

        private void ResetPlayerDrinkRig()
        {
            playerDrinkUpperArm = null;
            playerDrinkForearm = null;
            playerDrinkHand = null;
            playerDrinkRightGrip = null;
            playerDrinkMouth = null;
            playerDrinkOwnerRoot = null;
            playerDrinkHandAttachment = default;
            playerDrinkRigConfigured = false;
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

                    serviceView.ResetPresentation();
                    serviceView.SelectBottle(SelectedOffer.DrinkId);
                    if (UsesPhysicalMenu)
                    {
                        BeginPhysicalMenuDelivery(immediate: false);
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

            bool defersConsumption = UsesPhysicalMenu;
            DrinkPurchaseResult result;
            if (defersConsumption)
            {
                result = GameSessionState.TryOrderDrink(
                    SelectedOffer.DrinkId,
                    out pendingOrder);
            }
            else
            {
                pendingOrder = null;
                result = GameSessionState.TryPurchaseDrink(
                    SelectedOffer.DrinkId);
            }
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
                if (!RestPhysicalMenuAtCounter())
                {
                    throw new InvalidOperationException(
                        "A purchased bar menu could not close on the counter.");
                }
            }

            purchaseCommitted = true;
            beerPlacementCaptured = false;
            bottleGripAttached = false;
            bottleReturnCaptured = false;
            FeedbackKey = string.Empty;
            ResetCueState();
            if (!hasPhysicalPresentation)
            {
                RetroAudio.Play(RetroSfxId.DrinkGulp);
                RetroAudio.Play(RetroSfxId.UiConfirm);
                Close();
                return true;
            }

            DrinkId physicalRoute = UsesPhysicalMenu
                ? SelectedOffer.DrinkId
                : DrinkId.None;
            if (!timeline.Confirm(physicalRoute))
            {
                throw new InvalidOperationException(
                    "A committed drink purchase could not start service.");
            }

            activePresentation =
                BarDrinkPresentationCatalog.Get(SelectedOffer.DrinkId);
            if (!serviceView.ShowVesselForDrink(SelectedOffer.DrinkId))
            {
                throw new InvalidOperationException(
                    "The selected drink has no physical vessel.");
            }

            BarDrinkBottleView bottle = serviceView.SelectedBottle;
            bool usesBeerTap = timeline.IsBeerService;
            bool usesPhysicalBottle = timeline.IsBottleService;
            if (!usesBeerTap && !usesPhysicalBottle &&
                (bottle == null ||
                 !serviceView.ShowCarriedBottle(
                     activePresentation,
                     bottleCarrier)))
            {
                throw new InvalidOperationException(
                    "The selected drink has no carried bottle visual.");
            }

            if (!usesBeerTap)
            {
                if (bottle == null)
                {
                    throw new InvalidOperationException(
                        "The selected drink has no shelf bottle visual.");
                }

                bottleStartRotation = bottle.transform.rotation;
                bottleStartPosition = bottle.transform.position;
                if (servicePlan.TryGetBottleSlot(
                        SelectedOffer.DrinkId,
                        out BarDrinkBottleSlotPlan bottleSlot))
                {
                    bottleShelfReturnPosition =
                        serviceView.transform.TransformPoint(
                            bottleSlot.Pose.Position);
                    bottleShelfReturnRotation =
                        serviceView.transform.rotation *
                        bottleSlot.Pose.Rotation;
                }
                else
                {
                    bottleShelfReturnPosition = bottleStartPosition;
                    bottleShelfReturnRotation = bottleStartRotation;
                }
                if (usesPhysicalBottle)
                {
                    serviceView.HideCarriedBottle();
                    serviceView.ActiveVessel.gameObject.SetActive(false);
                    activeBottleHandTarget = Vector3.zero;
                    activeBottleReachCorrection = Vector3.zero;
                    activeBottleHandRadial = Vector3.right;
                    activeBottleWorldRotation = bottleStartRotation;
                }
                else if (bottleCarrier != null)
                {
                    // Place the visual-only copy around the real right-hand
                    // socket. Its surface contact, rather than its centreline,
                    // sits in the palm; subsequent presentation passes keep the
                    // scale-free copy on that socket without prop/hand separation.
                    serviceView.AlignCarriedBottleToCarrier(
                        bottleCarrier,
                        bottleStartRotation,
                        ResolveBottleHolderPosition());
                    bottleStartPosition =
                        serviceView.CarriedBottleRoot.position;
                }

                if (!usesPhysicalBottle)
                {
                    activeBottleHandTarget = bottleCarrier != null
                        ? bottleCarrier.position
                        : serviceView.ResolveCarriedBottleHandContact(
                            bottleStartPosition,
                            bottleStartRotation,
                            ResolveBottleHolderPosition());
                    activeBottleReachCorrection = Vector3.zero;
                    activeBottleHandRadial =
                        (serviceView.CarriedBottleHandContactWorldPosition -
                         serviceView.CarriedBottleGripCenterWorldPosition)
                        .normalized;
                    activeBottleWorldRotation = bottleStartRotation;
                }
            }
            else
            {
                serviceView.HideCarriedBottle();
                activeBottleHandTarget = Vector3.zero;
                activeBottleReachCorrection = Vector3.zero;
                activeBottleHandRadial = Vector3.right;
                activeBottleWorldRotation = Quaternion.identity;
            }
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

            if (UsesPhysicalMenu &&
                counterSeatView != null &&
                counterSeatView.Seat != null &&
                counterSeatView.Seat.IsSeated)
            {
                RestPhysicalMenuAtCounter();
                return;
            }

            if (timeline.Cancel())
            {
                FeedbackKey = string.Empty;
                if (UsesPhysicalMenu &&
                    counterMenuModel.BeginRetrieval())
                {
                    menuPresentation.BeginRetrieval();
                    menuDeliveryElapsedSeconds = 0f;
                    counterSeatView.EndMenuFocus();
                    counterMenuHint.Hide();
                }

                RetroAudio.Play(RetroSfxId.UiCancel);
                ApplyCurrentPresentation();
            }
        }

        public bool RestPhysicalMenuAtCounter()
        {
            bool confirmed = counterMenuModel != null &&
                counterMenuModel.State == CounterMenuState.Confirmed;
            if (!CanRestPhysicalMenu &&
                !confirmed)
            {
                return false;
            }

            if (!counterMenuModel.RestOnCounter())
            {
                return false;
            }

            if (!menuPresentation.RestOnCounter())
            {
                throw new InvalidOperationException(
                    "The bar menu could not rest at its counter dock.");
            }

            menuDeliveryElapsedSeconds = 0f;
            counterSeatView.EndMenuFocus();
            CinematicDepthOfField.EndImmediately();
            counterMenuHint?.Hide();
            restingMenuGazeArmed = false;
            inputUnlockFrame = Time.frameCount + 1;
            if (!confirmed)
            {
                RetroAudio.Play(RetroSfxId.UiCancel);
            }

            return true;
        }

        public bool ReopenPhysicalMenu()
        {
            if (!CanStandAfterMenuRested || !IsLookingAtRestingMenu ||
                !counterMenuModel.Reopen())
            {
                return false;
            }

            if (!menuPresentation.ReopenOnCounter())
            {
                throw new InvalidOperationException(
                    "The bar menu could not reopen at its counter dock.");
            }

            if (!serviceView.SelectBottle(SelectedOffer.DrinkId))
            {
                throw new InvalidOperationException(
                    "The reopened bar-menu selection has no shelf bottle.");
            }

            menuPresentation.SetSelection(SelectedIndex, false);
            restingMenuGazeArmed = false;
            BeginCounterMenuDepthOfField();
            counterSeatView.BeginMenuFocus(
                menuPresentation.ResolveCameraFocusPose(
                    counterSeatView.CurrentCameraPosition),
                BarDrinkMenuPresentation.CameraFocusFieldOfView);
            inputUnlockFrame = Time.frameCount + 1;
            RetroAudio.Play(RetroSfxId.UiConfirm);
            return true;
        }

        /// <summary>
        /// Starts the bartender's return trip only after the seat's shared
        /// interaction controller has completed the visible stand-up clip.
        /// </summary>
        public bool FinishSeatedSessionAfterExit()
        {
            if (!CanStandAfterMenuRested || !timeline.Cancel() ||
                !counterMenuModel.BeginRetrieval())
            {
                return false;
            }

            menuPresentation.BeginRetrieval();
            menuDeliveryElapsedSeconds = 0f;
            counterSeatView.EndMenuFocus();
            counterMenuHint?.Hide();
            ApplyCurrentPresentation();
            return true;
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
            timeline.Advance(unscaledDeltaTime);
            PlayCrossedCues();
            if (wasCommitted &&
                !timeline.IsCommitted &&
                timeline.IsBrowsing)
            {
                drinkActionPending =
                    playerInteraction != null &&
                    playerInteraction.IsNestedLoopActionActive;
                if (!drinkActionPending)
                {
                    CompleteOrderPresentation();
                }
            }
            else if (drinkActionPending && timeline.IsBrowsing &&
                     (playerInteraction == null ||
                      !playerInteraction.IsNestedLoopActionActive))
            {
                CompleteOrderPresentation();
            }

            AdvanceCounterMenuDelivery(unscaledDeltaTime);

            ApplyCurrentPresentation();
            if (timeline.Phase == BarDrinkServicePhase.Closed)
            {
                if (returningCounterMenuHome)
                {
                    return;
                }

                IsOpen = false;
                RestoreOwnedState();
                Closed?.Invoke(this);
            }
        }

        public bool BeginServedDrink()
        {
            if (!CanDrinkServedVessel || !IsLookingAtServedVessel ||
                playerInteraction == null)
            {
                return false;
            }

            PlayerAnimatedInteractionDefinition definition =
                CreatePlayerDrinkDefinition();
            if (!playerInteraction.BeginNestedLoopAction(definition))
            {
                return false;
            }

            if (!timeline.BeginDrink())
            {
                playerInteraction.CancelNestedLoopAction();
                return false;
            }

            drinkActionPending = true;
            playerDrinkStartedFrame = Time.frameCount;
            serviceView.ActiveVessel.SetInteractionHighlight(false);
            counterSeatView?.SetActionLookLocked(true);
            inputUnlockFrame = int.MaxValue;
            ApplyCurrentPresentation();
            return true;
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
                    UpdateRestingMenuGazeArm();
                    UpdateRestingMenuHighlight();
                    RefreshServedDrinkAffordance();
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

                bool isPlayerDrinkPhase =
                    IsPlayerDrinkPhase(timeline.Phase);
                float presentationDeltaTime = isPlayerDrinkPhase
                    ? Time.frameCount <= playerDrinkStartedFrame
                        ? 0f
                        : Time.deltaTime
                    : Time.unscaledDeltaTime;
                AdvancePresentation(presentationDeltaTime);
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
                    RestPhysicalMenuAtCounter();
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

        private void UpdateRestingMenuGazeArm()
        {
            if (restingMenuGazeArmed ||
                counterMenuModel == null ||
                counterMenuModel.State != CounterMenuState.Resting ||
                menuPresentation == null ||
                targetCamera == null)
            {
                return;
            }

            // Closing starts in a tight menu close-up. Require one observed
            // frame away from the resting prop before gaze can reopen it, so
            // the same E cannot immediately reverse the close action.
            if (!menuPresentation.IsLookingAtRestingMenu(targetCamera))
            {
                restingMenuGazeArmed = true;
            }
        }

        private void UpdateRestingMenuHighlight()
        {
            menuPresentation?.SetRestingHighlight(
                CanStandAfterMenuRested &&
                IsLookingAtRestingMenu &&
                !CounterMenuInput.IsBlockedByOtherUi());
        }

        public void RefreshServedDrinkAffordance()
        {
            BarDrinkVesselView vessel = serviceView?.ActiveVessel;
            if (vessel == null)
            {
                return;
            }

            vessel.SetInteractionHighlight(
                IsLookingAtServedVessel &&
                !CounterMenuInput.IsBlockedByOtherUi());
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
                OffsetServicePosition(
                    servicePlan.BottlePourPose.Position));
            CinematicDepthOfField.Begin(
                Vector3.Distance(
                    targetCamera.transform.position,
                    depthOfFieldFocusPoint),
                4f);
        }

        private void BeginCounterMenuDepthOfField()
        {
            if (!UsesPhysicalMenu || targetCamera == null ||
                menuPresentation == null)
            {
                return;
            }

            // The page is the only seated-bar shot that needs a cinematic
            // focus override. Delivery, service and the resting booklet stay
            // on the restrained room grade so nearby people remain sharp.
            CinematicDepthOfField.Begin(
                Vector3.Distance(
                    targetCamera.transform.position,
                    menuPresentation.CameraFocusWorldPosition),
                CounterMenuDepthOfFieldAperture,
                CounterMenuDepthOfFieldFocalLength);
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
            ApplyActionEyeClearance(frame);
            ApplyCounterSeatDepthOfField();
            // The bartender owns the bottle now. During a real counter-seat
            // session the old camera-local arm meshes stay hidden; their
            // attachment rig remains active only to carry the vessel through
            // the established drinking arc.
            firstPersonArms.ApplyPresentation(
                frame.ArmsVisibility,
                0f,
                frame.DrinkLift,
                renderVisuals: counterSeatView == null);
            ApplyPlayerVisualForFrame(frame);
            if (timeline.IsBottleService)
            {
                // The pour root is solved from the live vessel target. Update
                // that vessel first so the bottle and stream use this frame's
                // counter pose instead of following it one frame behind.
                ApplyVesselPresentation(frame);
                ApplyBottlePresentation(frame);
                RefreshBottlePourStreamAfterGrip();
            }
            else
            {
                ApplyBottlePresentation(frame);
                ApplyVesselPresentation(frame);
            }
        }

        private void ApplyActionEyeClearance(BarDrinkServiceFrame frame)
        {
            if (counterSeatView == null)
            {
                return;
            }

            float weight;
            switch (frame.Phase)
            {
                case BarDrinkServicePhase.PlayerPickup:
                    weight = SmoothRange(
                        frame.PhaseProgress,
                        0.68f,
                        0.90f);
                    break;
                case BarDrinkServicePhase.PlayerDrinking:
                    weight = 1f;
                    break;
                case BarDrinkServicePhase.PlayerVesselReturn:
                    weight = 1f - SmoothRange(
                        frame.PhaseProgress,
                        0.25f,
                        0.62f);
                    break;
                default:
                    weight = 0f;
                    break;
            }

            counterSeatView.SetActionEyeClearance(weight);
        }

        private void ApplyCounterSeatDepthOfField()
        {
            if (counterSeatView == null || targetCamera == null ||
                counterMenuModel == null ||
                counterMenuModel.State != CounterMenuState.Open ||
                menuPresentation == null ||
                !CinematicDepthOfField.IsActive)
            {
                return;
            }

            CinematicDepthOfField.SetFocusDistance(
                Vector3.Distance(
                    targetCamera.transform.position,
                    menuPresentation.CameraFocusWorldPosition));
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

        private void BeginPhysicalMenuDelivery(bool immediate)
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
            menuDeliveryElapsedSeconds = 0f;
            menuPlacementStarted = false;
            counterServerAtTarget = menuPresentation.Carrier == null;
            restingMenuGazeArmed = false;
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
            menuDeliveryElapsedSeconds = 0f;
            menuPresentation.SetSelection(SelectedIndex, false);
            BeginCounterMenuDepthOfField();
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

            bool carryHome = counterSeatView != null &&
                menuPresentation.Carrier != null;
            menuPresentation.CompleteRetrieval(carryHome);
            counterMenuModel.CompleteRetrieval();
            returningCounterMenuHome = carryHome;
            counterServerAtTarget = false;
            menuPlacementStarted = false;
            menuDeliveryElapsedSeconds = 0f;
            counterMenuHint?.Hide();
        }

        private void AdvanceCounterMenuDelivery(float unscaledDeltaTime)
        {
            if (counterMenuModel == null ||
                counterMenuModel.State != CounterMenuState.Delivering)
            {
                return;
            }

            if (!counterServerAtTarget)
            {
                return;
            }

            if (!menuPlacementStarted)
            {
                menuPlacementStarted = true;
                menuDeliveryElapsedSeconds = 0f;
                // Recapture the carrier at the chosen stool. The first
                // BeginDelivery attached the closed booklet at home for the
                // visible walk along the counter.
                menuPresentation.BeginDelivery();
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
            if (timeline.IsBottleService)
            {
                ApplyPhysicalBottlePresentation(frame);
                return;
            }

            if (!serviceView.IsCarriedBottleVisible ||
                !timeline.IsCommitted)
            {
                activeBottleHandTarget = Vector3.zero;
                activeBottleReachCorrection = Vector3.zero;
                return;
            }

            bool bottleIsHeld =
                frame.Phase == BarDrinkServicePhase.BottlePickup ||
                frame.Phase == BarDrinkServicePhase.VesselPlacement ||
                frame.Phase == BarDrinkServicePhase.Pouring ||
                frame.Phase == BarDrinkServicePhase.BottleReturn ||
                frame.Phase == BarDrinkServicePhase.Drinking ||
                frame.Phase == BarDrinkServicePhase.VesselReturn;
            if (!bottleIsHeld)
            {
                activeBottleHandTarget = Vector3.zero;
                activeBottleReachCorrection = Vector3.zero;
                return;
            }

            // The bottle never flies to the hero: the bartender's arm
            // carries it from the shelf to the pour spot over the
            // counter, with a small lift arc so the pickup reads as a
            // grab rather than a slide.
            BarDrinkServicePose pourLocal =
                ResolveActiveServiceLocalPose(
                    servicePlan.BottlePourPose);
            Vector3 pourPosition =
                serviceView.transform.TransformPoint(
                    pourLocal.Position);
            Quaternion pourRotation =
                serviceView.transform.rotation * pourLocal.Rotation;
            Vector3 travelPosition = Vector3.Lerp(
                bottleStartPosition,
                pourPosition,
                frame.BottleTravel);
            travelPosition.y +=
                Mathf.Sin(Mathf.Clamp01(frame.BottleTravel) *
                          Mathf.PI) * 0.10f;
            Quaternion travelRotation = Quaternion.Slerp(
                bottleStartRotation,
                pourRotation,
                frame.BottleTilt);
            activeBottleWorldRotation = travelRotation;
            Vector3 holderPosition = ResolveBottleHolderPosition();
            Vector3 requestedHandTarget =
                serviceView.ResolveCarriedBottleHandContact(
                    travelPosition,
                    travelRotation,
                    holderPosition);
            activeBottleHandTarget = ClampBottleGripToReach(
                bottleReachShoulder != null
                    ? bottleReachShoulder.position
                    : requestedHandTarget,
                ResolveBottleGripReachLimit(),
                requestedHandTarget);
            activeBottleReachCorrection =
                activeBottleHandTarget - requestedHandTarget;

            if (bottleCarrier != null)
            {
                // Re-solving its world offset after each timeline rotation
                // keeps the 6 cm surface contact exactly under the real socket
                // while avoiding the imported bone hierarchy's 100x scale.
                serviceView.AlignCarriedBottleToCarrier(
                    bottleCarrier,
                    travelRotation,
                    holderPosition);
            }
            else
            {
                serviceView.SetCarriedBottleWorldPose(
                    travelPosition + activeBottleReachCorrection,
                    travelRotation,
                    holderPosition);
            }

            activeBottleHandRadial =
                (serviceView.CarriedBottleHandContactWorldPosition -
                 serviceView.CarriedBottleGripCenterWorldPosition).normalized;
        }

        private void ApplyPhysicalBottlePresentation(
            BarDrinkServiceFrame frame)
        {
            bool carriesBottle =
                frame.Phase == BarDrinkServicePhase.BottlePickup ||
                frame.Phase == BarDrinkServicePhase.BottleCarryToPour ||
                frame.Phase == BarDrinkServicePhase.VesselPlacement ||
                frame.Phase == BarDrinkServicePhase.Pouring ||
                frame.Phase == BarDrinkServicePhase.BottleCarryToGuest ||
                frame.Phase ==
                    BarDrinkServicePhase.BottleVesselPlacement ||
                frame.Phase ==
                    BarDrinkServicePhase.BottleWalkToShelfReturn ||
                frame.Phase == BarDrinkServicePhase.BottleReturn;
            if (!timeline.IsCommitted || !carriesBottle)
            {
                if (serviceView.IsCarriedBottleVisible)
                {
                    serviceView.HideCarriedBottle();
                    serviceView.SelectedBottle?.ResetExact();
                }

                bottleGripAttached = false;
                bottleReturnCaptured = false;
                activeBottleHandTarget = Vector3.zero;
                activeBottleReachCorrection = Vector3.zero;
                return;
            }

            if (!serviceView.IsCarriedBottleVisible)
            {
                activeBottleHandTarget = Vector3.zero;
                activeBottleReachCorrection = Vector3.zero;
                return;
            }

            switch (frame.Phase)
            {
                case BarDrinkServicePhase.BottlePickup:
                    ApplyBottleShelfPickup(frame.PhaseProgress);
                    break;
                case BarDrinkServicePhase.Pouring:
                    bottleReturnCaptured = false;
                    ApplyBottlePourPose(frame.BottleTilt);
                    break;
                case BarDrinkServicePhase.BottleReturn:
                    ApplyBottleShelfReturn(frame.PhaseProgress);
                    break;
                default:
                    bottleGripAttached = true;
                    bottleReturnCaptured = false;
                    activeBottleHandTarget = Vector3.zero;
                    activeBottleReachCorrection = Vector3.zero;
                    activeBottleWorldRotation = bottleStartRotation;
                    break;
            }
        }

        private void ApplyBottleShelfPickup(float progress)
        {
            activeBottleWorldRotation = bottleStartRotation;
            Vector3 requestedHandTarget =
                serviceView.ResolveCarriedBottleHandContact(
                    bottleStartPosition,
                    bottleStartRotation,
                    ResolveBottleHolderPosition());
            SetActiveBottleHandTarget(requestedHandTarget);
            if (!bottleGripAttached)
            {
                serviceView.SetCarriedBottleWorldPose(
                    bottleStartPosition,
                    bottleStartRotation,
                    ResolveBottleHolderPosition());
                if (progress >= BottleGripAttachProgress)
                {
                    if (bottleCarrier != null)
                    {
                        serviceView.AlignCarriedBottleToCarrier(
                            bottleCarrier,
                            bottleStartRotation,
                            ResolveBottleHolderPosition());
                    }
                    else
                    {
                        serviceView.SetCarriedBottleWorldPose(
                            bottleStartPosition +
                            activeBottleReachCorrection,
                            bottleStartRotation,
                            ResolveBottleHolderPosition());
                    }

                    RefreshBottleHandRadial();
                    bottleGripAttached = true;
                }
            }

            RefreshBottleHandRadial();
        }

        private void ApplyBottlePourPose(float tilt)
        {
            Pose pour = ResolveBottlePourWorldPose();
            activeBottleWorldRotation = Quaternion.Slerp(
                bottleStartRotation,
                pour.rotation,
                Mathf.Clamp01(tilt));
            Vector3 bottleRootPosition =
                ResolveBottlePourRootWorldPosition(
                    activeBottleWorldRotation);
            Vector3 holderPosition = ResolveBottleHolderPosition();
            // The anatomical hand is solved later in LateUpdate. Put the
            // service prop on the intended mouth-to-vessel line now as well,
            // so Update-time stream evaluation cannot expose the previous
            // frame's diagonal before the post-IK contact pass runs.
            serviceView.SetCarriedBottleWorldPose(
                bottleRootPosition,
                activeBottleWorldRotation,
                holderPosition);
            Vector3 requestedHandTarget =
                serviceView.ResolveCarriedBottleHandContact(
                    bottleRootPosition,
                    activeBottleWorldRotation,
                    holderPosition);
            SetActiveBottleHandTarget(requestedHandTarget);
            RefreshBottleHandRadial();
        }

        private void ApplyBottleShelfReturn(float progress)
        {
            activeBottleWorldRotation = bottleShelfReturnRotation;
            if (!bottleReturnCaptured)
            {
                bottleReturnHandStart = bottleCarrier != null
                    ? bottleCarrier.position
                    : serviceView.ResolveCarriedBottleHandContact();
                bottleReturnCaptured = true;
            }

            Vector3 shelfHandTarget =
                serviceView.ResolveCarriedBottleHandContact(
                    bottleShelfReturnPosition,
                    bottleShelfReturnRotation,
                    ResolveBottleHolderPosition());
            SetActiveBottleHandTarget(Vector3.Lerp(
                bottleReturnHandStart,
                shelfHandTarget,
                Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress))));
            RefreshBottleHandRadial();
        }

        private void SetActiveBottleHandTarget(Vector3 requestedTarget)
        {
            activeBottleHandTarget = ClampBottleGripToReach(
                bottleReachShoulder != null
                    ? bottleReachShoulder.position
                    : requestedTarget,
                ResolveBottleGripReachLimit(),
                requestedTarget);
            activeBottleReachCorrection =
                activeBottleHandTarget - requestedTarget;
        }

        private void RefreshBottleHandRadial()
        {
            Vector3 radial =
                serviceView.CarriedBottleHandContactWorldPosition -
                serviceView.CarriedBottleGripCenterWorldPosition;
            if (radial.sqrMagnitude > 0.000001f)
            {
                activeBottleHandRadial = radial.normalized;
            }
        }

        private void ApplyVesselPresentation(BarDrinkServiceFrame frame)
        {
            if (timeline.IsBeerService)
            {
                ApplyBeerVesselPresentation(frame);
                return;
            }

            if (timeline.IsBottleService)
            {
                ApplyBottleServiceVesselPresentation(frame);
                return;
            }

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
                        ResolveActiveServiceLocalPose(
                            servicePlan.VesselCounterPose);
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
                    // The bartender slides the vessel in flat from the hand
                    // side of this stool; VesselVisibility is the slide.
                    BarDrinkServicePose counter =
                        ResolveActiveServiceLocalPose(
                            servicePlan.VesselCounterPose);
                    float slide = Mathf.SmoothStep(
                        0f,
                        1f,
                        visibility);
                    Vector3 counterPosition =
                        serviceView.transform.TransformPoint(
                            counter.Position);
                    Vector3 entryOffset =
                        serviceView.transform.TransformVector(
                            ResolveVesselSlideEntryOffset());
                    serviceView.SetActiveVesselWorldPose(
                        Vector3.Lerp(
                            counterPosition + entryOffset,
                            counterPosition,
                            slide),
                        serviceView.transform.rotation *
                        counter.Rotation);
                }

                serviceView.SetFillProgress(frame.VesselFill);
            }

            if (frame.StreamVisibility > 0.002f)
            {
                Color streamColor = activePresentation.LiquidColor;
                streamColor.a = frame.StreamVisibility;
                serviceView.SetPourStreamFromCarriedBottle(
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

        private void ApplyBottleServiceVesselPresentation(
            BarDrinkServiceFrame frame)
        {
            BarDrinkVesselView vessel = serviceView.ActiveVessel;
            if (vessel == null)
            {
                serviceView.HidePourStream();
                serviceView.SetBartenderVesselContact(false, 0f);
                return;
            }

            bool visible =
                frame.Phase == BarDrinkServicePhase.VesselPlacement ||
                frame.Phase == BarDrinkServicePhase.Pouring ||
                frame.Phase == BarDrinkServicePhase.BottleCarryToGuest ||
                frame.Phase ==
                    BarDrinkServicePhase.BottleVesselPlacement ||
                frame.Phase ==
                    BarDrinkServicePhase.BottleWalkToShelfReturn ||
                frame.Phase == BarDrinkServicePhase.BottleReturn ||
                frame.Phase == BarDrinkServicePhase.AwaitingDrink ||
                frame.Phase == BarDrinkServicePhase.PlayerPickup ||
                frame.Phase == BarDrinkServicePhase.PlayerDrinking ||
                frame.Phase ==
                    BarDrinkServicePhase.PlayerVesselReturn ||
                frame.Phase == BarDrinkServicePhase.EmptyOnCounter;
            vessel.gameObject.SetActive(visible);
            if (!visible)
            {
                serviceView.HidePourStream();
                serviceView.SetBartenderVesselContact(false, 0f);
                beerPlacementCaptured = false;
                return;
            }

            vessel.transform.localScale = vesselBaseScale;
            bool carriedByBartender =
                frame.Phase ==
                    BarDrinkServicePhase.BottleCarryToGuest;
            serviceView.SetBartenderVesselContact(
                carriedByBartender,
                carriedByBartender ? 1f : 0f);
            switch (frame.Phase)
            {
                case BarDrinkServicePhase.VesselPlacement:
                    ApplyBottlePreparationPlacement(
                        frame.VesselVisibility,
                        vessel);
                    beerPlacementCaptured = false;
                    break;
                case BarDrinkServicePhase.Pouring:
                    Pose prep = ResolveBottlePreparationVesselWorldPose();
                    vessel.SetWorldPose(prep.position, prep.rotation);
                    beerPlacementCaptured = false;
                    break;
                case BarDrinkServicePhase.BottleCarryToGuest:
                    beerPlacementCaptured = false;
                    break;
                case BarDrinkServicePhase.BottleVesselPlacement:
                    ApplyBottleVesselPlacement(
                        frame.PhaseProgress,
                        vessel);
                    break;
                case BarDrinkServicePhase.PlayerPickup:
                case BarDrinkServicePhase.PlayerDrinking:
                case BarDrinkServicePhase.PlayerVesselReturn:
                    ApplyServedVesselForPlayerAction(vessel, frame);
                    break;
                default:
                    PlaceServedVesselOnCounter();
                    beerPlacementCaptured = false;
                    break;
            }

            serviceView.SetFillProgress(frame.VesselFill);
            if (frame.Phase == BarDrinkServicePhase.Pouring &&
                frame.StreamVisibility > 0.002f)
            {
                Color streamColor = activePresentation.LiquidColor;
                streamColor.a = frame.StreamVisibility;
                serviceView.SetPourStreamFromCarriedBottle(
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

        private void ApplyBottlePreparationPlacement(
            float progress,
            BarDrinkVesselView vessel)
        {
            Pose prep = ResolveBottlePreparationVesselWorldPose();
            float amount = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(progress));
            Vector3 entry = serviceView.transform.TransformVector(
                Vector3.forward * 0.32f);
            vessel.SetWorldPose(
                Vector3.Lerp(prep.position + entry, prep.position, amount),
                prep.rotation);
        }

        private void ApplyBottleVesselPlacement(
            float progress,
            BarDrinkVesselView vessel)
        {
            if (!beerPlacementCaptured)
            {
                beerPlacementStartPosition = vessel.transform.position;
                beerPlacementStartRotation = vessel.transform.rotation;
                beerPlacementCaptured = true;
            }

            Pose counter = ResolveServedCounterWorldPose(vessel);
            float amount = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(progress));
            vessel.SetWorldPose(
                Vector3.Lerp(
                    beerPlacementStartPosition,
                    counter.position,
                    amount),
                Quaternion.Slerp(
                    beerPlacementStartRotation,
                    counter.rotation,
                    amount));
        }

        private void ApplyBeerVesselPresentation(
            BarDrinkServiceFrame frame)
        {
            BarDrinkVesselView vessel = serviceView.ActiveVessel;
            if (vessel == null)
            {
                serviceView.HidePourStream();
                return;
            }

            bool servicePhase =
                frame.Phase == BarDrinkServicePhase.BeerWalkToTap ||
                frame.Phase == BarDrinkServicePhase.BeerGlassPickup ||
                frame.Phase == BarDrinkServicePhase.BeerPouring ||
                frame.Phase == BarDrinkServicePhase.BeerCarryToGuest ||
                frame.Phase == BarDrinkServicePhase.BeerGlassPlacement ||
                frame.Phase == BarDrinkServicePhase.AwaitingDrink ||
                frame.Phase == BarDrinkServicePhase.PlayerPickup ||
                frame.Phase == BarDrinkServicePhase.PlayerDrinking ||
                frame.Phase == BarDrinkServicePhase.PlayerVesselReturn ||
                frame.Phase == BarDrinkServicePhase.EmptyOnCounter;
            vessel.gameObject.SetActive(servicePhase);
            if (!servicePhase)
            {
                serviceView.HidePourStream();
                serviceView.SetBeerTapHandlePull(0f);
                return;
            }

            vessel.transform.localScale = vesselBaseScale;
            serviceView.SetBeerTapHandlePull(frame.TapHandlePull);
            switch (frame.Phase)
            {
                case BarDrinkServicePhase.BeerWalkToTap:
                    serviceView.SetActiveVesselAtBeerTap();
                    beerPlacementCaptured = false;
                    break;
                case BarDrinkServicePhase.BeerGlassPickup:
                    serviceView.SetActiveVesselAtBeerTap(
                        frame.PhaseProgress);
                    beerPlacementCaptured = false;
                    break;
                case BarDrinkServicePhase.BeerPouring:
                    serviceView.SetActiveVesselAtBeerTap(1f);
                    beerPlacementCaptured = false;
                    break;
                case BarDrinkServicePhase.BeerCarryToGuest:
                    beerPlacementCaptured = false;
                    break;
                case BarDrinkServicePhase.BeerGlassPlacement:
                    ApplyBeerGlassPlacement(frame.PhaseProgress, vessel);
                    break;
                case BarDrinkServicePhase.PlayerPickup:
                    ApplyServedVesselForPlayerAction(vessel, frame);
                    break;
                case BarDrinkServicePhase.PlayerDrinking:
                    ApplyServedVesselForPlayerAction(vessel, frame);
                    break;
                case BarDrinkServicePhase.PlayerVesselReturn:
                    ApplyServedVesselForPlayerAction(vessel, frame);
                    break;
                default:
                    PlaceServedVesselOnCounter();
                    beerPlacementCaptured = false;
                    break;
            }

            serviceView.SetFillProgress(frame.VesselFill);
            if (frame.Phase == BarDrinkServicePhase.BeerPouring &&
                frame.StreamVisibility > 0.002f)
            {
                Color streamColor = activePresentation.LiquidColor;
                streamColor.a = frame.StreamVisibility;
                serviceView.SetPourStreamFromBeerTap(
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

        private void ApplyBeerGlassPlacement(
            float progress,
            BarDrinkVesselView vessel)
        {
            if (!beerPlacementCaptured)
            {
                beerPlacementStartPosition = vessel.transform.position;
                beerPlacementStartRotation = vessel.transform.rotation;
                beerPlacementCaptured = true;
            }

            Pose counter = ResolveServedCounterWorldPose(vessel);
            float amount = Mathf.SmoothStep(0f, 1f, progress);
            vessel.SetWorldPose(
                Vector3.Lerp(
                    beerPlacementStartPosition,
                    counter.position,
                    amount),
                Quaternion.Slerp(
                    beerPlacementStartRotation,
                    counter.rotation,
                    amount));
        }

        private void ApplyServedVesselForPlayerAction(
            BarDrinkVesselView vessel,
            BarDrinkServiceFrame frame)
        {
            if (!playerDrinkRigConfigured)
            {
                PlaceServedVesselOnCounter();
                return;
            }

            float carryAmount;
            float tipAmount;
            float handWeight;
            switch (frame.Phase)
            {
                case BarDrinkServicePhase.PlayerPickup:
                    carryAmount = SmoothRange(
                        frame.PhaseProgress,
                        0.48f,
                        0.90f);
                    tipAmount = SmoothRange(
                        frame.PhaseProgress,
                        0.58f,
                        0.90f);
                    handWeight = SmoothRange(
                        frame.PhaseProgress,
                        0.22f,
                        0.48f);
                    break;
                case BarDrinkServicePhase.PlayerDrinking:
                    carryAmount = 1f;
                    tipAmount = 1f;
                    handWeight = 1f;
                    break;
                case BarDrinkServicePhase.PlayerVesselReturn:
                    carryAmount = 1f - SmoothRange(
                        frame.PhaseProgress,
                        0.25f,
                        0.78f);
                    tipAmount = 1f - SmoothRange(
                        frame.PhaseProgress,
                        0f,
                        0.25f);
                    handWeight = frame.PhaseProgress < 0.78f ? 1f : 0f;
                    break;
                default:
                    PlaceServedVesselOnCounter();
                    return;
            }

            Pose counter = ResolveServedCounterWorldPose(vessel);
            if (!vessel.TryResolveDrinkPose(
                    playerDrinkMouth,
                    playerDrinkOwnerRoot,
                    tipAmount,
                    out Pose drinkPose))
            {
                PlaceServedVesselOnCounter();
                return;
            }

            Vector3 position = Vector3.Lerp(
                counter.position,
                drinkPose.position,
                carryAmount);
            position += playerDrinkOwnerRoot.up *
                        (Mathf.Sin(carryAmount * Mathf.PI) * 0.035f);
            vessel.SetWorldPose(
                position,
                Quaternion.Slerp(
                    counter.rotation,
                    drinkPose.rotation,
                    tipAmount));
            SolveRightHandToVesselGrip(vessel, handWeight);
        }

        private void SolveRightHandToVesselGrip(
            BarDrinkVesselView vessel,
            float weight)
        {
            if (weight <= 0f || !playerDrinkRigConfigured)
            {
                return;
            }

            Quaternion socketRotation = ResolveRightMugSocketRotation(
                playerDrinkOwnerRoot.right,
                vessel.OpeningDirection);
            Quaternion handRotation = socketRotation *
                Quaternion.Inverse(
                    playerDrinkHandAttachment.SocketRotationInHand);
            Vector3 handPosition = vessel.GripWorldPosition -
                handRotation *
                playerDrinkHandAttachment.SocketPositionInHand;
            Vector3 elbowHint = playerDrinkUpperArm.position +
                playerDrinkOwnerRoot.right * 0.42f -
                playerDrinkOwnerRoot.forward * 0.08f -
                playerDrinkOwnerRoot.up * 0.12f;
            LimbTwoBoneIk.Solve(
                playerDrinkUpperArm,
                playerDrinkForearm,
                playerDrinkHand,
                handPosition,
                handRotation,
                elbowHint,
                Mathf.Clamp01(weight),
                float.PositiveInfinity,
                true);
        }

        private static Quaternion ResolveRightMugSocketRotation(
            Vector3 ownerRight,
            Vector3 openingDirection)
        {
            Vector3 opening = openingDirection.normalized;
            Vector3 outward = Vector3.ProjectOnPlane(
                ownerRight,
                opening);
            if (outward.sqrMagnitude < 0.000001f)
            {
                outward = Vector3.Cross(opening, Vector3.forward);
            }

            if (outward.sqrMagnitude < 0.000001f)
            {
                outward = Vector3.Cross(opening, Vector3.right);
            }

            outward.Normalize();
            return Quaternion.LookRotation(
                Vector3.Cross(outward, opening).normalized,
                -opening);
        }

        private static float SmoothRange(
            float value,
            float start,
            float end)
        {
            return Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(start, end, value));
        }

        private void PlaceServedVesselOnCounter()
        {
            BarDrinkVesselView vessel = serviceView.ActiveVessel;
            if (vessel == null)
            {
                return;
            }

            Pose counter = ResolveServedCounterWorldPose(vessel);
            vessel.SetWorldPose(counter.position, counter.rotation);
        }

        internal Pose ResolveServedCounterWorldPose(
            BarDrinkVesselView vessel)
        {
            BarDrinkServicePose localCounter =
                ResolveActiveServiceLocalPose(
                    servicePlan.VesselCounterPose);
            Pose counter = new Pose(
                serviceView.transform.TransformPoint(
                    localCounter.Position +
                    Vector3.forward * BeerVesselServerwardOffset),
                serviceView.transform.rotation * localCounter.Rotation);
            return playerDrinkRigConfigured &&
                   vessel.TryResolveRightHandUprightPose(
                       counter.position,
                       playerDrinkOwnerRoot,
                       out Pose rightHandled)
                ? rightHandled
                : counter;
        }

        internal Pose ResolveBeerCounterWorldPose(
            BarDrinkVesselView vessel)
        {
            return ResolveServedCounterWorldPose(vessel);
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
            menuDeliveryElapsedSeconds = 0f;
            menuPlacementStarted = false;
            counterServerAtTarget = false;
            returningCounterMenuHome = false;
            restingMenuGazeArmed = false;
            menuPresentation?.ResetPresentation();
            serviceView?.ResetPresentation();
            activeBottleHandTarget = Vector3.zero;
            activeBottleReachCorrection = Vector3.zero;
            activeBottleHandRadial = Vector3.right;
            activeBottleWorldRotation = Quaternion.identity;
            beerPlacementCaptured = false;
            bottleGripAttached = false;
            bottleReturnCaptured = false;
            bottleReturnHandStart = Vector3.zero;
            bottleShelfReturnPosition = Vector3.zero;
            bottleShelfReturnRotation = Quaternion.identity;
            firstPersonArms?.Hide();
            playerInteraction?.CancelNestedLoopAction();
            counterSeatView?.SetActionLookLocked(false);
            RestorePlayerVisual();
            RestoreCameraState();
            RestoreSceneMarkers();
            modalLock.Restore();
            purchaseCommitted = false;
            pendingOrder = null;
            drinkActionPending = false;
            playerDrinkStartedFrame = -1;
            ResetCueState();
            Physics.SyncTransforms();

            // Release the selected stool only after its camera/menu state has
            // been restored. The bartender choreography then walks back to
            // its authored home point while the next station remains free to
            // configure a different service offset.
            counterSeatView = null;
            activeServiceLocalOffset = Vector3.zero;
            activeServiceMirrored = false;
        }

        private void CompleteOrderPresentation()
        {
            CommitPendingDrink();
            hoveredBottle = null;
            serviceView.HidePourStream();
            serviceView.HideCarriedBottle();
            if (!timeline.HasEmptyVessel)
            {
                serviceView.HideVessel();
            }
            else
            {
                serviceView.SetFillProgress(0f);
            }
            if (serviceView.SelectedBottle != null)
            {
                if (UsesPhysicalMenu && timeline.IsBottleService)
                {
                    serviceView.SelectedBottle.ResetExact();
                }
                else
                {
                    serviceView.ResetSelectedBottle();
                }
            }
            else
            {
                serviceView.SelectBottle(SelectedOffer.DrinkId);
            }

            purchaseCommitted = false;
            counterSeatView?.SetActionLookLocked(false);
            drinkActionPending = false;
            playerDrinkStartedFrame = -1;
            activeBottleHandTarget = Vector3.zero;
            activeBottleReachCorrection = Vector3.zero;
            activeBottleHandRadial = Vector3.right;
            activeBottleWorldRotation = Quaternion.identity;
            bottleGripAttached = false;
            bottleReturnCaptured = false;
            bottleReturnHandStart = Vector3.zero;
            bottleShelfReturnPosition = Vector3.zero;
            bottleShelfReturnRotation = Quaternion.identity;
            FeedbackKey = string.Empty;
            inputUnlockFrame = Time.frameCount + 1;
            ResetCueState();
            if (UsesPhysicalMenu)
            {
                if (counterMenuModel.State ==
                    CounterMenuState.Confirmed)
                {
                    if (!counterMenuModel.RestOnCounter() ||
                        !menuPresentation.RestOnCounter())
                    {
                        throw new InvalidOperationException(
                            "The served bar menu could not remain on the counter.");
                    }
                }
            }

            Physics.SyncTransforms();
        }

        private void CommitPendingDrink()
        {
            if (pendingOrder == null)
            {
                return;
            }

            if (!GameSessionState.TryConsumeOrderedDrink(pendingOrder))
            {
                throw new InvalidOperationException(
                    "A completed physical drink could not commit its effects.");
            }

            pendingOrder = null;
        }

        private static PlayerAnimatedInteractionDefinition
            CreatePlayerDrinkDefinition()
        {
            return CounterSeatInteraction.CreateBarDrinkDefinition();
        }

        private void RestoreCameraState()
        {
            if (counterSeatView != null)
            {
                CinematicDepthOfField.EndImmediately();
            }
            else
            {
                CinematicDepthOfField.End();
            }
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

        private Vector3 OffsetServicePosition(Vector3 localPosition)
        {
            return ResolveActiveServiceLocalPosition(localPosition);
        }

        private Vector3 ResolveVesselSlideEntryOffset()
        {
            return Vector3.right *
                   (activeServiceMirrored
                       ? -VesselSlideEntryDistance
                       : VesselSlideEntryDistance);
        }

        public static Vector3 ClampBottleGripToReach(
            Vector3 shoulderPosition,
            float reachLimit,
            Vector3 requestedTarget)
        {
            if (float.IsNaN(reachLimit) || reachLimit <= 0f ||
                float.IsInfinity(reachLimit))
            {
                return requestedTarget;
            }

            Vector3 fromShoulder = requestedTarget - shoulderPosition;
            float distance = fromShoulder.magnitude;
            if (distance <= reachLimit || distance < 0.000001f)
            {
                return requestedTarget;
            }

            return shoulderPosition +
                   fromShoulder * (reachLimit / distance);
        }

        private float ResolveBottleGripReachLimit()
        {
            if (bottleReachShoulder == null ||
                bottleReachElbow == null ||
                bottleReachWrist == null ||
                bottleCarrier == null)
            {
                return float.PositiveInfinity;
            }

            float fullReach =
                Vector3.Distance(
                    bottleReachShoulder.position,
                    bottleReachElbow.position) +
                Vector3.Distance(
                    bottleReachElbow.position,
                    bottleReachWrist.position) +
                Vector3.Distance(
                    bottleReachWrist.position,
                    bottleCarrier.position);
            return Mathf.Max(0.001f, fullReach - BottleGripReachMargin);
        }

        private Vector3 ResolveBottleHolderPosition()
        {
            if (bottleReachShoulder != null)
            {
                return bottleReachShoulder.position;
            }

            if (bottleHolderRoot != null)
            {
                return bottleHolderRoot.position +
                       bottleHolderRoot.up * 1.2f;
            }

            return serviceView != null && servicePlan != null
                ? serviceView.transform.TransformPoint(
                    servicePlan.BottleHandPose.Position)
                : transform.position;
        }

        private void PlayCrossedCues()
        {
            if (!timeline.IsCommitted)
            {
                return;
            }

            BarDrinkServicePhase phase = timeline.Phase;
            if (!clinkPlayed &&
                (HasBottleVesselPlacementStarted(phase) ||
                 phase == BarDrinkServicePhase.BeerGlassPlacement ||
                 phase == BarDrinkServicePhase.AwaitingDrink ||
                 phase == BarDrinkServicePhase.PlayerPickup ||
                 phase == BarDrinkServicePhase.PlayerDrinking ||
                 phase == BarDrinkServicePhase.PlayerVesselReturn))
            {
                clinkPlayed = true;
                RetroAudio.Play(RetroSfxId.Clink);
            }

            if (!pourPlayed &&
                (HasBottlePourStarted(phase) ||
                 phase == BarDrinkServicePhase.BeerPouring ||
                 phase == BarDrinkServicePhase.BeerCarryToGuest ||
                 phase == BarDrinkServicePhase.BeerGlassPlacement ||
                 phase == BarDrinkServicePhase.AwaitingDrink ||
                 phase == BarDrinkServicePhase.PlayerPickup ||
                 phase == BarDrinkServicePhase.PlayerDrinking ||
                 phase == BarDrinkServicePhase.PlayerVesselReturn))
            {
                pourPlayed = true;
                RetroAudio.Play(RetroSfxId.Pour);
            }

            if (!gulpPlayed &&
                (phase == BarDrinkServicePhase.Drinking ||
                 phase == BarDrinkServicePhase.VesselReturn ||
                 phase == BarDrinkServicePhase.PlayerDrinking ||
                 phase == BarDrinkServicePhase.PlayerVesselReturn))
            {
                gulpPlayed = true;
                RetroAudio.Play(RetroSfxId.DrinkGulp);
            }
        }

        private static bool HasBottleVesselPlacementStarted(
            BarDrinkServicePhase phase)
        {
            return phase == BarDrinkServicePhase.VesselPlacement ||
                   HasBottlePourStarted(phase);
        }

        private static bool HasBottlePourStarted(
            BarDrinkServicePhase phase)
        {
            return phase == BarDrinkServicePhase.Pouring ||
                   phase == BarDrinkServicePhase.BottleCarryToGuest ||
                   phase ==
                       BarDrinkServicePhase.BottleVesselPlacement ||
                   phase ==
                       BarDrinkServicePhase.BottleWalkToShelfReturn ||
                   phase == BarDrinkServicePhase.BottleReturn ||
                   phase == BarDrinkServicePhase.Drinking ||
                   phase == BarDrinkServicePhase.VesselReturn;
        }

        private static bool IsPlayerDrinkPhase(
            BarDrinkServicePhase phase)
        {
            return phase == BarDrinkServicePhase.PlayerPickup ||
                   phase == BarDrinkServicePhase.PlayerDrinking ||
                   phase == BarDrinkServicePhase.PlayerVesselReturn;
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
