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
    public sealed partial class BarDrinkShopController : MonoBehaviour
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
        private PaymentNoticeView paymentNotice;
        public PaymentNoticeView PaymentNotice => paymentNotice;
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
        private BarPlayerDrinkReach playerDrinkReach;
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
        private Pose emptyVesselCounterPose;
        private Pose emptyVesselStowStartPose;
        private Quaternion emptyVesselRotationInServer;
        private Pose emptyVesselStorageServerPose;
        private BarBartenderPresentation bartenderPresentation;
        private Vector3 bartenderCarryShoulderLocal;
        private Vector3 bartenderServiceHomeLocalPosition;
        private float bartenderCarryArmLength;
        private bool pendingCounterMenuDelivery;
        private bool pendingCounterMenuRetrieval;
        private bool pendingOrderedDrinkPresentation;
        private bool emptyVesselStowCaptured;
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
        public const float EmptyVesselBackStepDistance = .25f;
        // One shared handoff band for the standing server and seated hero.
        public const float ServedVesselServerwardOffset = 0.12f;
        public const float ServedVesselGuestLipClearance = 0.05f;
        private const float ServedVesselSideClearance = 0.05f;
        public const float VesselSurfaceClearance = 0.002f;
        public const float VesselCarryClearance = 0.08f;
        private const float RestingMenuCarryClearance = 0.02f;
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
        /// <summary>Business hours gate new orders; IsOpen remains the seated/menu session lifetime.</summary>
        public bool IsAcceptingOrders => BarBusinessHours.IsOpen(GameSessionState.GameMinuteOfDay);
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
            hasPhysicalPresentation && ActiveServiceTimeline != null
                ? ActiveServiceTimeline.Phase
                : IsOpen
                    ? BarDrinkServicePhase.Browsing
                    : BarDrinkServicePhase.Closed;
        public bool IsBrowsing =>
            IsOpen && !repeatRequestPending && !menuOrderRequestPending &&
            (!hasPhysicalPresentation ||
             (timeline.IsBrowsing &&
              (!UsesPhysicalMenu ||
               counterMenuModel.State == CounterMenuState.Open)));
        public bool IsServing =>
            IsOpen && hasPhysicalPresentation &&
            (timeline.IsCommitted || drinkActionPending || repeatRequestPending || menuOrderRequestPending);
        public bool CanDrinkServedVessel =>
            IsOpen && UsesPhysicalMenu && timeline != null &&
            timeline.CanBeginDrink && HeroServiceVessel != null;
        public bool IsLookingAtServedVessel =>
            CanDrinkServedVessel && targetCamera != null &&
            !CounterMenuInput.IsBlockedByOtherUi() &&
            HeroServiceVessel.IsLookingAt(targetCamera);
        public bool PurchaseCommitted => purchaseCommitted;
        public bool IsClearingEmptyVessel => ActiveServiceTimeline != null && ActiveServiceTimeline.IsClearingEmptyVessel;
        public int ClearedEmptyVesselCount { get; private set; }
        public bool IsBartenderServiceReady =>
            (IsPatronServiceActive ? ReferenceEquals(timeline, patronServiceTimeline) :
                !WantsHeroBartenderService || serviceQueue.IsOwner(this)) &&
            (bartenderPresentation == null || bartenderPresentation.IsTowelServiceReady);
        public bool WantsBartenderService => IsPatronServiceActive ||
            WantsHeroBartenderService && serviceQueue.IsOwner(this);
        private bool WantsHeroBartenderService
        {
            get
            {
                if (!hasPhysicalPresentation) return false;
                if (returningCounterMenuHome || pendingCounterMenuDelivery ||
                    pendingCounterMenuRetrieval || pendingOrderedDrinkPresentation) return true;
                if (!IsOpen || timeline == null) return false;
                if (counterMenuModel != null && (counterMenuModel.State == CounterMenuState.Delivering ||
                    counterMenuModel.State == CounterMenuState.Retrieving)) return true;
                switch (timeline.Phase)
                {
                    case BarDrinkServicePhase.Closed:
                    case BarDrinkServicePhase.Browsing:
                    case BarDrinkServicePhase.AwaitingDrink:
                    case BarDrinkServicePhase.PlayerPickup:
                    case BarDrinkServicePhase.PlayerDrinking:
                    case BarDrinkServicePhase.PlayerVesselReturn:
                    case BarDrinkServicePhase.EmptyOnCounter:
                    case BarDrinkServicePhase.Drinking:
                    case BarDrinkServicePhase.VesselReturn:
                        return false;
                    default:
                        return true;
                }
            }
        }
        public float EmptyVesselRetractionError => IsClearingEmptyVessel && serviceView?.ActiveVessel != null
            ? Vector3.Distance(serviceView.ActiveVessel.transform.position,
                ResolveEmptyVesselCarryPose(serviceView.ActiveVessel).position)
            : float.PositiveInfinity;
        public float EmptyVesselStorageFacingError => IsClearingEmptyVessel && bottleHolderRoot != null
            ? Quaternion.Angle(bottleHolderRoot.rotation, emptyVesselStorageServerPose.rotation) : 0f;
        public BarDrinkServiceTimeline Timeline => ActiveServiceTimeline;
        public BarDrinkServiceView ServiceView => serviceView;
        public BarDrinkFirstPersonArms FirstPersonArms => firstPersonArms;
        public BarPlayerDrinkReach PlayerDrinkReach => playerDrinkReach;
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
        public bool HasCounterServiceTarget => IsPatronServiceActive || counterSeatView != null &&
            IsOpen && !returningCounterMenuHome;
        public Vector3 ActiveServiceLocalOffset => activeServiceLocalOffset;
        public bool ActiveServiceMirrored => activeServiceMirrored;
        public bool IsReturningCounterMenuHome =>
            !IsPatronServiceActive && returningCounterMenuHome;
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
            playerDrinkReach != null ? playerDrinkReach.GripError :
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
            paymentNotice = intoxicationHud != null ? PaymentNoticeView.Attach(intoxicationHud) : null;
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
            if (!StoreHeroServiceOffsetIfPatronActive(serviceLocalOffset, mirrorServiceHorizontally))
            {
                activeServiceLocalOffset = serviceLocalOffset;
                activeServiceMirrored = mirrorServiceHorizontally;
            }
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

            Vector3 menuDockOffset = serviceLocalOffset;
            if (mirrorServiceHorizontally)
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

            bool returningBottle = ActiveServiceTimeline != null &&
                (ActiveServiceTimeline.Phase ==
                     BarDrinkServicePhase.BottleWalkToShelfReturn ||
                 ActiveServiceTimeline.Phase == BarDrinkServicePhase.BottleReturn);
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

            if (ActiveServiceTimeline.IsRepeatService && bottleHolderRoot != null)
            {
                Vector3 local = ResolveBeerGuestServerLocalPosition(bartenderServiceHomeLocalPosition);
                Transform parent = bottleHolderRoot.parent;
                return new Pose(parent != null ? parent.TransformPoint(local) : local,
                    serviceView.transform.rotation * Quaternion.Euler(0f, 180f, 0f));
            }

            Vector3 vesselLocal = ResolveBottlePreparationLocalPosition();
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
            if (ActiveServiceTimeline.IsRepeatService) return emptyVesselCounterPose;
            BarDrinkServicePose prep =
                servicePlan.BottlePreparationVesselPose;
            Pose proposed = new Pose(
                serviceView.transform.TransformPoint(
                    ResolveBottlePreparationLocalPosition()),
                serviceView.transform.rotation * prep.Rotation);
            return ResolveSupportedVesselPose(serviceView.ActiveVessel, proposed);
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
            return servicePlan.ResolveBottlePreparationPosition(
                servicePlan.SeatPose.Position.x + activeServiceLocalOffset.x);
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
            BarDrinkServiceTimeline serviceTimeline = ActiveServiceTimeline;
            if (atTarget && serviceTimeline != null && serviceTimeline.IsRepeatService &&
                serviceTimeline.Phase == BarDrinkServicePhase.BeerWalkToTap)
                repeatTapStartPose = ResolveEmptyVesselCarryPose(serviceView.ActiveVessel);
            return serviceTimeline != null && serviceTimeline.ReportBeerServerAtTap(atTarget);
        }

        public bool ReportEmptyVesselServerAtGuest(bool atTarget)
        {
            if (IsPatronCupOfferActive) return false;
            if (GameTimeScaleRuntime.IsPaused || ActiveServiceTimeline == null || !ActiveServiceTimeline.ReportEmptyVesselServerAtGuest(atTarget)) return false;
            Transform server = bottleHolderRoot;
            emptyVesselRotationInServer = server != null
                ? Quaternion.Inverse(server.rotation) * emptyVesselCounterPose.rotation
                : emptyVesselCounterPose.rotation;
            if (server != null)
                emptyVesselStorageServerPose = new Pose(
                    server.position - server.forward * EmptyVesselBackStepDistance,
                    server.rotation);
            return true;
        }

        public bool ReportEmptyVesselServerAtStorage(bool atTarget) =>
            !GameTimeScaleRuntime.IsPaused && ActiveServiceTimeline != null && ActiveServiceTimeline.ReportEmptyVesselServerAtStorage(atTarget);

        public bool ReportEmptyVesselGripContact(bool inContact)
        {
            bool accepted = !GameTimeScaleRuntime.IsPaused && ActiveServiceTimeline != null &&
                ActiveServiceTimeline.ReportEmptyVesselGrip(inContact);
            if (accepted && IsPatronServiceActive) activePatronRefill.Drinking.ReleaseCounterCupOffer();
            return accepted;
        }

        public void ConfigureBartenderPresentation(BarBartenderPresentation owner)
        {
            bartenderPresentation = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
            bartenderServiceHomeLocalPosition = owner.transform.localPosition;
            BarBartenderAssetRegistry registry = owner.Registry;
            bartenderCarryShoulderLocal = owner.transform.InverseTransformPoint(registry.LeftUpperArm.position);
            bartenderCarryArmLength = LimbTwoBoneIk.ChainLength(registry.LeftUpperArm,
                registry.LeftForearm, registry.LeftHand);
        }

        public void RefreshEmptyVesselForServerPose()
        {
            if (IsClearingEmptyVessel)
                ApplyEmptyVesselCleanup(ActiveServiceTimeline.CurrentFrame);
            else if (ActiveServiceTimeline != null && ActiveServiceTimeline.IsRepeatService &&
                     ActiveServiceTimeline.Phase == BarDrinkServicePhase.BeerWalkToTap)
                ApplyRepeatBeerCarry();
        }

        public bool TryBeginEmptyVesselCarry()
        {
            if (GameTimeScaleRuntime.IsPaused || ActiveServiceTimeline == null ||
                ActiveServiceTimeline.Phase != BarDrinkServicePhase.EmptyVesselPickup ||
                ActiveServiceTimeline.PhaseProgress < 1f || serviceView.ActiveVessel == null) return false;
            BarDrinkVesselView vessel = serviceView.ActiveVessel;
            Pose carry = ResolveEmptyVesselCarryPose(vessel);
            return ActiveServiceTimeline.ReportEmptyVesselRetracted(
                Vector3.Distance(vessel.transform.position, carry.position) <= .012f);
        }

        public Pose ResolveEmptyVesselStorageServerWorldPose()
        {
            return emptyVesselStorageServerPose;
        }

        /// <summary>The old physical vessel is released only after the counter hides it.</summary>
        public bool TryCompleteEmptyVesselStorage()
        {
            if (GameTimeScaleRuntime.IsPaused || timeline == null || timeline.Phase != BarDrinkServicePhase.EmptyVesselStow ||
                timeline.PhaseProgress < 1f || !IsEmptyVesselBehindCounter()) return false;
            if (!timeline.ReportEmptyVesselStored(true)) return false;
            serviceView.SetBartenderVesselContact(false, 0f);
            serviceView.HideVessel();
            ClearedEmptyVesselCount++;
            emptyVesselStowCaptured = false;
            PrepareOrderedDrinkPresentation();
            ApplyCurrentPresentation();
            return true;
        }

        public bool ReportBeerServerAtGuest(bool atTarget)
        {
            return ActiveServiceTimeline != null && ActiveServiceTimeline.ReportBeerServerAtGuest(atTarget);
        }

        public bool ReportBottleServerAtShelf(bool atTarget)
        {
            if (ActiveServiceTimeline == null || !atTarget ||
                ActiveServiceTimeline.Phase !=
                    BarDrinkServicePhase.BottleWalkToShelf ||
                !ActiveServiceTimeline.ReportBottleServerAtShelf(true))
            {
                return false;
            }

            BarDrinkBottleView bottle = serviceView?.SelectedBottle;
            if (bottle == null ||
                !serviceView.ShowCarriedBottle(
                    BarDrinkPresentationCatalog.Get(bottle.DrinkId),
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
            activeBottleWorldRotation = bottleStartRotation;
            RefreshBottleHandRadial();
            bottleGripAttached = false;
            bottleReturnCaptured = false;
            return true;
        }

        public bool ReportBottleServerAtPour(bool atTarget)
        {
            return ActiveServiceTimeline != null && ActiveServiceTimeline.ReportBottleServerAtPour(atTarget);
        }

        public bool ReportBottleServerAtGuest(bool atTarget)
        {
            return ActiveServiceTimeline != null && ActiveServiceTimeline.ReportBottleServerAtGuest(atTarget);
        }

        public bool ReportBottleServerAtShelfReturn(bool atTarget)
        {
            if (ActiveServiceTimeline == null ||
                !ActiveServiceTimeline.ReportBottleServerAtShelfReturn(atTarget))
            {
                return false;
            }

            bottleReturnCaptured = false;
            ApplyBottleShelfReturn(0f);
            return true;
        }

        internal void RefreshBottlePourStreamAfterGrip()
        {
            if (ActiveServiceTimeline == null || !ActiveServiceTimeline.IsBottleService ||
                ActiveServiceTimeline.Phase != BarDrinkServicePhase.Pouring ||
                serviceView == null)
            {
                return;
            }

            float visibility = ActiveServiceTimeline.CurrentFrame.StreamVisibility;
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

            firstPersonArms.Initialize(targetCamera,
                (playerRuntime.Visual as Player3DCharacterPresentation)?.Registry);
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
            playerDrinkReach = BarPlayerDrinkReach.Attach(playerRuntime, playerInteraction);
            repeatGesture = BarRepeatGesture.Attach(playerRuntime, playerInteraction);
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
            if (!CheckBusinessHours() || IsOpen ||
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
                if (!IsPatronServiceActive) ResetCueState();
                if (hasPhysicalPresentation)
                {
                    bartenderPresentation?.RequestTowelService(true);
                    if (counterSeatView == null)
                    {
                        CaptureCameraPath();
                        CapturePlayerVisualState();
                    }

                    if (!IsPatronServiceActive)
                    {
                        serviceView.ResetPresentation();
                        serviceView.SelectBottle(SelectedOffer.DrinkId);
                    }
                    if (UsesPhysicalMenu)
                    {
                        pendingCounterMenuDelivery = true;
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
            if (hasPhysicalPresentation && !IsPatronServiceActive)
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

            if (!CheckBusinessHours()) return false;

            if (UsesPhysicalMenu) return BeginMenuOrderRequest();

            return CommitSelection();
        }

        private bool CommitSelection()
        {
            // Revalidate at the cash transaction as well as at menu input.
            if (!CheckBusinessHours()) return false;
            pendingOrder = null;
            DrinkPurchaseResult result = GameSessionState.TryPurchaseDrink(SelectedOffer.DrinkId);
            if (!result.Succeeded)
            {
                FeedbackKey = GetFailureKey(result.Status);
                counterMenuHint?.ShowStatus(FeedbackKey);
                RetroAudio.Play(RetroSfxId.Bad);
                return false;
            }

            paymentNotice?.Show(result.CashBefore - result.CashAfter, result.CashAfter);

            purchaseCommitted = true;
            currentOrderDrinkId = SelectedOffer.DrinkId;
            beerPlacementCaptured = false;
            bottleGripAttached = false;
            bottleReturnCaptured = false;
            FeedbackKey = string.Empty;
            ResetCueState();
            if (!hasPhysicalPresentation)
            {
                RetroAudio.Play(RetroSfxId.DrinkGulp);
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
            bartenderPresentation?.RequestTowelService(true);
            if (timeline.IsClearingEmptyVessel)
            {
                BarDrinkVesselView empty = serviceView.ActiveVessel;
                if (empty == null || !empty.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("A returned empty vessel must remain present until the bartender collects it.");
                emptyVesselCounterPose = new Pose(empty.transform.position, empty.transform.rotation);
                // The actual pickup pose supplies the short backward step.
                emptyVesselStorageServerPose = default;
                emptyVesselStowCaptured = false;
                vesselBaseScale = empty.transform.localScale;
                serviceView.HidePourStream();
                serviceView.HideCarriedBottle();
            }
            else
            {
                pendingOrderedDrinkPresentation = !IsBartenderServiceReady;
                if (!pendingOrderedDrinkPresentation)
                    PrepareOrderedDrinkPresentation();
            }
            inputUnlockFrame = int.MaxValue;
            ApplyCurrentPresentation();
            return true;
        }

        private bool CheckBusinessHours()
        {
            if (IsAcceptingOrders)
            {
                if (FeedbackKey == "interaction.bar_closed")
                {
                    FeedbackKey = string.Empty;
                    counterMenuHint?.ClearStatus();
                }
                return true;
            }
            FeedbackKey = "interaction.bar_closed";
            counterMenuHint?.ShowStatus(FeedbackKey);
            return false;
        }

        private void PrepareOrderedDrinkPresentation()
        {
            if (!timeline.IsRepeatService && !serviceView.ShowVesselForDrink(CurrentOrderDrinkId))
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
                        CurrentOrderDrinkId,
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
                    if (!timeline.IsRepeatService)
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
                    activeBottleWorldRotation = bottleStartRotation;
                    RefreshBottleHandRadial();
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
                    BeginPhysicalMenuRetrievalWhenReady();
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

            if (!IsPatronServiceActive && !serviceView.SelectBottle(SelectedOffer.DrinkId))
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

            BeginPhysicalMenuRetrievalWhenReady();
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
            ResolveServiceOrder(this, false);
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

        public void AdvancePresentation(float deltaTime)
        {
            if (!IsOpen || !hasPhysicalPresentation)
            {
                return;
            }

            if (!UpdateHeroServiceReservation()) return;
            if (AdvanceMenuOrderRequest() || repeatRequestPending) return;
            if (IsPatronServiceActive)
            {
                AdvanceHeroDrinkWhilePatronServed(deltaTime);
                return;
            }

            if (WantsBartenderService && !IsBartenderServiceReady) return;
            if (pendingCounterMenuDelivery)
            {
                pendingCounterMenuDelivery = false;
                BeginPhysicalMenuDelivery(immediate: false);
            }
            if (pendingCounterMenuRetrieval)
            {
                pendingCounterMenuRetrieval = false;
                menuPresentation.BeginRetrieval();
            }
            if (pendingOrderedDrinkPresentation)
            {
                pendingOrderedDrinkPresentation = false;
                PrepareOrderedDrinkPresentation();
            }

            bool wasCommitted = timeline.IsCommitted;
            if (UsesPhysicalMenu && counterMenuModel.State == CounterMenuState.Retrieving &&
                !counterServerAtTarget) deltaTime = 0f;
            if (playerInteraction != null && playerInteraction.IsWaitingForMouthAccess)
                deltaTime = 0f;
            if (playerDrinkReach != null &&
                (timeline.Phase == BarDrinkServicePhase.PlayerPickup &&
                    (!playerDrinkReach.PickupContactObserved || playerDrinkReach.CarryProgress < .999f) ||
                 timeline.Phase == BarDrinkServicePhase.PlayerVesselReturn &&
                    (!playerDrinkReach.ReturnContactObserved || !playerDrinkReach.IsReturnPoseRestored)))
                deltaTime = Mathf.Min(deltaTime, Mathf.Max(0f,
                    timeline.PhaseDurationSeconds - timeline.PhaseElapsedSeconds - .001f));
            timeline.Advance(deltaTime);
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

            AdvanceCounterMenuDelivery(deltaTime);

            ApplyCurrentPresentation();
            NotifyHeroServiceDelivery();
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
            BarDrinkVesselView heroVessel = HeroServiceVessel;
            heroPlayerDrinkCounterPose = new Pose(heroVessel.transform.position, heroVessel.transform.rotation);
            heroConcurrentGulpPlayed = false;
            playerDrinkReach?.PrepareForDrink(heroVessel,
                heroPlayerDrinkCounterPose, serviceView.CounterSurfaceWorldBounds);
            if (!playerInteraction.BeginNestedLoopAction(definition))
            {
                playerDrinkReach?.ResetPresentation();
                return false;
            }

            if (!timeline.BeginDrink())
            {
                playerInteraction.CancelNestedLoopAction();
                playerDrinkReach?.ResetPresentation();
                return false;
            }

            drinkActionPending = true;
            playerDrinkStartedFrame = Time.frameCount;
            heroVessel.SetInteractionHighlight(false);
            counterSeatView?.SetActionLookLocked(true, allowLimitedLook: true);
            inputUnlockFrame = int.MaxValue;
            ApplyCurrentPresentation();
            return true;
        }

        private void Update()
        {
            AdvancePatronService(Time.deltaTime);
            if (!IsOpen)
            {
                return;
            }

            if (hasPhysicalPresentation)
            {
                if (repeatRequestPending && (repeatGesture == null || !repeatGesture.IsActive))
                    repeatRequestPending = false;
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
                float presentationDeltaTime = isPlayerDrinkPhase &&
                    Time.frameCount <= playerDrinkStartedFrame
                        ? 0f
                        : Time.deltaTime;
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
                if (IsPatronServiceActive) ApplyConcurrentHeroDrinkPose();
                else ApplyCurrentPresentation();
            }
        }

        private void OnDisable()
        {
            CancelPatronService();
            Close();
        }

        private void OnDestroy()
        {
            CancelPatronService();
            Close();
            Closed = null;
            ServiceOrderAccepted = null;
            ServiceOrderDelivered = null;
            ServiceOrderCancelled = null;
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

            bool showRepeatFailure = IsLookingAtRepeatVessel && !string.IsNullOrEmpty(FeedbackKey);
            if ((!IsBrowsing && !showRepeatFailure) ||
                CounterMenuInput.IsBlockedByOtherUi())
            {
                counterMenuHint.Hide();
                return;
            }

            if (!IsAcceptingOrders)
            {
                counterMenuHint.ShowStatus("interaction.bar_closed");
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
            BarDrinkVesselView vessel = HeroServiceVessel;
            if (vessel == null)
            {
                return;
            }

            vessel.SetInteractionHighlight(
                (IsLookingAtServedVessel || IsLookingAtRepeatVessel) &&
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
            if (IsPatronServiceActive || !IsOpen || !hasPhysicalPresentation || timeline == null ||
                WantsHeroBartenderService && !IsBartenderServiceReady)
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

        private void BeginPhysicalMenuRetrievalWhenReady()
        {
            counterServerAtTarget = false;
            bartenderPresentation?.RequestTowelService(true);
            pendingCounterMenuRetrieval = !IsBartenderServiceReady;
            if (!pendingCounterMenuRetrieval)
                menuPresentation.BeginRetrieval();
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

        private void AdvanceCounterMenuDelivery(float deltaTime)
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
                deltaTime);
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

            RefreshBottleHandRadial();
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
            Vector3 radial = serviceView.IsCarriedBottleVisible
                ? serviceView.ResolveCarriedBottleHandRadial(serviceView.CarriedBottleRoot.position,
                    activeBottleWorldRotation, ResolveBottleHolderPosition())
                : Vector3.zero;
            if (radial.sqrMagnitude > 0.000001f)
            {
                activeBottleHandRadial = radial.normalized;
            }
        }

        private void ApplyEmptyVesselCleanup(BarDrinkServiceFrame frame)
        {
            if (IsPatronCupOfferActive) return;
            BarDrinkVesselView vessel = serviceView.ActiveVessel;
            if (vessel == null) return;
            vessel.gameObject.SetActive(true);
            vessel.SetInteractionHighlight(false);
            serviceView.SetFillProgress(0f);
            serviceView.HidePourStream();
            bool attached = ActiveServiceTimeline.EmptyVesselGripConfirmed;
            serviceView.SetBartenderVesselContact(attached, 1f);
            Pose pose = emptyVesselCounterPose;
            switch (frame.Phase)
            {
                case BarDrinkServicePhase.EmptyVesselPickup:
                    float lift = SmoothRange(frame.PhaseProgress,
                        BarDrinkServiceTimeline.EmptyVesselPickupContactProgress, .62f);
                    float retract = SmoothRange(frame.PhaseProgress, .62f, 1f);
                    Pose carry = ResolveEmptyVesselCarryPose(vessel);
                    Vector3 raised = emptyVesselCounterPose.position;
                    raised.y = ResolveMinimumVesselRootY(vessel, pose.rotation, VesselCarryClearance, true);
                    pose.position = Vector3.Lerp(emptyVesselCounterPose.position, raised, lift);
                    pose.position = Vector3.Lerp(pose.position, carry.position, retract);
                    break;
                case BarDrinkServicePhase.EmptyVesselCarryToStorage:
                    pose = ResolveEmptyVesselCarryPose(vessel);
                    break;
                case BarDrinkServicePhase.EmptyVesselStow:
                    if (!emptyVesselStowCaptured)
                    {
                        emptyVesselStowStartPose = new Pose(vessel.transform.position, vessel.transform.rotation);
                        emptyVesselStowCaptured = true;
                    }
                    Pose hidden = ResolveEmptyVesselStoragePose(vessel, true);
                    // The backward step already clears the inner lip. Give
                    // the free palm time to brace, then lower vertically.
                    float lower = SmoothRange(frame.PhaseProgress, .35f, 1f);
                    pose.position = emptyVesselStowStartPose.position;
                    pose.position.y = Mathf.Lerp(emptyVesselStowStartPose.position.y, hidden.position.y, lower);
                    pose.rotation = emptyVesselStowStartPose.rotation;
                    break;
            }
            vessel.SetWorldPose(pose.position, pose.rotation);
        }

        private Pose ResolveEmptyVesselCarryPose(BarDrinkVesselView vessel)
        {
            Quaternion rotation = bottleHolderRoot != null
                ? bottleHolderRoot.rotation * emptyVesselRotationInServer
                : emptyVesselCounterPose.rotation;
            Vector3 localGrip = vessel.transform.InverseTransformPoint(vessel.GripWorldPosition);
            Vector3 gripOffset = rotation * Vector3.Scale(localGrip, vessel.transform.lossyScale);
            Vector3 position = emptyVesselCounterPose.position;
            if (bottleHolderRoot != null && bartenderPresentation != null)
            {
                Bounds shell = vessel.LocalVisualBounds;
                Vector3 scale = vessel.transform.lossyScale;
                Vector3 centreOffset = rotation * Vector3.Scale(shell.center, scale);
                Vector3 right = bottleHolderRoot.right;
                float halfWidth = 0f;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 extent = rotation * Vector3.Scale(Vector3.Scale(shell.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f,
                            (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f)), scale);
                    halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(extent, right)));
                }
                float side = bartenderCarryShoulderLocal.x < 0f ? -1f : 1f;
                float sideDistance = Mathf.Max(.30f, Mathf.Abs(bartenderCarryShoulderLocal.x) + halfWidth + .06f);
                // Carry low and just ahead of the hip. A high target in the
                // shoulder plane folds the elbow back against the coat.
                position = bottleHolderRoot.position + right * (side * sideDistance) +
                    bottleHolderRoot.forward * .15f - centreOffset;
                position.y = bottleHolderRoot.TransformPoint(bartenderCarryShoulderLocal).y -
                    bartenderCarryArmLength * .60f - gripOffset.y;
            }
            position.y = Mathf.Max(position.y, ResolveMinimumVesselRootY(vessel, rotation,
                VesselCarryClearance, true));
            return new Pose(position, rotation);
        }

        private Pose ResolveEmptyVesselStoragePose(BarDrinkVesselView vessel, bool hidden)
        {
            Bounds visual = vessel != null ? ResolveVesselWorldBounds(vessel) : new Bounds(Vector3.zero, Vector3.one * .16f);
            Quaternion rotation = emptyVesselStowCaptured ? emptyVesselStowStartPose.rotation : emptyVesselCounterPose.rotation;
            float y = hidden
                ? serviceView.CounterSurfaceWorldHeight - .16f -
                  (visual.max.y - (vessel != null ? vessel.transform.position.y : 0f))
                : vessel != null ? ResolveMinimumVesselRootY(vessel, rotation, VesselCarryClearance, true)
                    : serviceView.CounterSurfaceWorldHeight + VesselCarryClearance;
            Vector3 start = emptyVesselStowCaptured
                ? emptyVesselStowStartPose.position : vessel.transform.position;
            return new Pose(new Vector3(start.x, y, start.z), rotation);
        }

        private Bounds ResolveCounterOcclusionBounds()
        {
            Bounds top = serviceView.CounterSurfaceWorldBounds;
            if (top.size.x < .1f || top.size.z < .1f)
                top = new Bounds(serviceView.transform.TransformPoint(
                    servicePlan.VesselCounterPose.Position + Vector3.forward * .32f), new Vector3(6.85f, .06f, 1f));
            Vector3 minimum = top.min, maximum = top.max;
            minimum.x += .04f; maximum.x -= .04f;
            minimum.z += .04f; maximum.z -= .04f;
            minimum.y = serviceView.transform.position.y;
            maximum.y = serviceView.CounterSurfaceWorldHeight - .025f;
            top.SetMinMax(minimum, maximum);
            return top;
        }

        private static Bounds ResolveVesselWorldBounds(BarDrinkVesselView vessel)
        {
            Bounds local = vessel.LocalVisualBounds;
            Bounds result = new Bounds(vessel.transform.TransformPoint(local.min), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
                result.Encapsulate(vessel.transform.TransformPoint(local.center + Vector3.Scale(local.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f))));
            return result;
        }

        private bool IsEmptyVesselBehindCounter()
        {
            BarDrinkVesselView vessel = serviceView.ActiveVessel;
            if (vessel == null || !vessel.gameObject.activeInHierarchy) return false;
            Bounds visual = ResolveVesselWorldBounds(vessel);
            Bounds counter = ResolveCounterOcclusionBounds();
            if (visual.max.y > serviceView.CounterSurfaceWorldHeight - .08f || visual.min.z <= counter.max.z)
                return false;
            if (targetCamera == null) return true;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = visual.center + Vector3.Scale(visual.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                Vector3 ray = point - targetCamera.transform.position;
                if (!counter.IntersectRay(new Ray(targetCamera.transform.position, ray.normalized), out float distance) ||
                    distance >= ray.magnitude - .005f) return false;
            }
            return true;
        }

        private void ApplyVesselPresentation(BarDrinkServiceFrame frame)
        {
            // The nested order gesture owns the existing empty cup through
            // its release and hand return, before the accepted-service handoff.
            if (!IsPatronServiceActive && repeatGesture != null && repeatGesture.IsActive && timeline.HasEmptyVessel)
                return;
            if (timeline.IsClearingEmptyVessel)
            {
                ApplyEmptyVesselCleanup(frame);
                return;
            }
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
                timeline.IsRepeatService ||
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
                    if (timeline.IsRepeatService)
                        vessel.SetWorldPose(emptyVesselCounterPose.position, emptyVesselCounterPose.rotation);
                    else ApplyBottlePreparationPlacement(frame.VesselVisibility, vessel);
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
                    if (timeline.IsRepeatService && frame.Phase != BarDrinkServicePhase.AwaitingDrink &&
                        frame.Phase != BarDrinkServicePhase.EmptyOnCounter)
                        vessel.SetWorldPose(emptyVesselCounterPose.position, emptyVesselCounterPose.rotation);
                    else PlaceServedVesselOnCounter();
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
            Pose entryPose = new Pose(prep.position + entry, prep.rotation);
            Pose staged = ResolveVesselPlacementPose(vessel, entryPose, prep, amount);
            vessel.SetWorldPose(staged.position, staged.rotation);
        }

        private float ResolveMinimumVesselRootY(
            BarDrinkVesselView vessel, Quaternion rotation, float clearance,
            bool clearsRestingMenu = false)
        {
            Bounds bounds = vessel.LocalVisualBounds;
            Vector3 scale = vessel.transform.lossyScale;
            float lowest = float.PositiveInfinity;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 signs = new Vector3((corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                Vector3 local = bounds.center + Vector3.Scale(bounds.extents, signs);
                lowest = Mathf.Min(lowest,
                    (rotation * Vector3.Scale(local, scale)).y);
            }
            float supportHeight = serviceView.CounterSurfaceWorldHeight + clearance;
            if (clearsRestingMenu &&
                serviceView.TryGetRestingMenuBounds(out Bounds menuBounds))
            {
                // Choose the transfer height before travelling horizontally,
                // so entering the book footprint never creates a height jump.
                supportHeight = Mathf.Max(supportHeight,
                    menuBounds.max.y + RestingMenuCarryClearance);
            }
            return supportHeight - lowest;
        }

        private Pose ResolveSupportedVesselPose(BarDrinkVesselView vessel, Pose proposed)
        {
            if (vessel != null)
                proposed.position.y = ResolveMinimumVesselRootY(
                    vessel, proposed.rotation, VesselSurfaceClearance);
            return proposed;
        }

        /// <summary>Raise the carried shell above the counter and resting menu.</summary>
        public Vector3 ResolveBartenderVesselCarryTarget(Vector3 requestedHandPosition)
        {
            BarDrinkVesselView vessel = serviceView?.ActiveVessel;
            if (vessel == null)
                return requestedHandPosition;
            Quaternion rotation = ResolveServedCounterWorldPose(vessel).rotation;
            Vector3 localGrip = vessel.transform.InverseTransformPoint(vessel.GripWorldPosition);
            Vector3 gripOffset = rotation * Vector3.Scale(localGrip, vessel.transform.lossyScale);
            requestedHandPosition.y = Mathf.Max(requestedHandPosition.y,
                ResolveMinimumVesselRootY(vessel, rotation,
                    VesselCarryClearance, true) + gripOffset.y);
            return requestedHandPosition;
        }

        private Pose ResolveVesselPlacementPose(
            BarDrinkVesselView vessel, Pose start, Pose end, float progress)
        {
            float time = Mathf.Clamp01(progress);
            float lift = SmoothRange(time, 0f, 0.20f);
            float travel = SmoothRange(time, 0.20f, 0.70f);
            float lower = SmoothRange(time, 0.70f, 1f);
            Quaternion rotation = Quaternion.Slerp(start.rotation, end.rotation, travel);
            float hoverY = Mathf.Max(Mathf.Max(start.position.y, end.position.y),
                ResolveMinimumVesselRootY(vessel, rotation,
                    VesselCarryClearance, true));
            Vector3 position = Vector3.Lerp(start.position, end.position, travel);
            position.y = Mathf.Lerp(Mathf.Lerp(start.position.y, hoverY, lift),
                end.position.y, lower);
            // Both endpoint supports are exact, while horizontal travel keeps
            // the complete scaled shell above the counter and closed booklet.
            position.y = Mathf.Max(position.y,
                ResolveMinimumVesselRootY(vessel, rotation, VesselSurfaceClearance));
            return new Pose(position, rotation);
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
            Pose staged = ResolveVesselPlacementPose(vessel,
                new Pose(beerPlacementStartPosition, beerPlacementStartRotation),
                counter, progress);
            vessel.SetWorldPose(staged.position, staged.rotation);
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
                    if (timeline.IsRepeatService) ApplyRepeatBeerCarry();
                    else serviceView.SetActiveVesselAtBeerTap();
                    beerPlacementCaptured = false;
                    break;
                case BarDrinkServicePhase.BeerGlassPickup:
                    serviceView.SetActiveVesselAtBeerTap(
                        frame.PhaseProgress);
                    if (timeline.IsRepeatService)
                    {
                        Pose tap = new Pose(vessel.transform.position, vessel.transform.rotation);
                        float travel = Mathf.SmoothStep(0f, 1f, frame.PhaseProgress);
                        vessel.SetWorldPose(Vector3.Lerp(repeatTapStartPose.position, tap.position, travel),
                            Quaternion.Slerp(repeatTapStartPose.rotation, tap.rotation, travel));
                    }
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
            Pose staged = ResolveVesselPlacementPose(vessel,
                new Pose(beerPlacementStartPosition, beerPlacementStartRotation),
                counter, progress);
            vessel.SetWorldPose(staged.position, staged.rotation);
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

            if (playerDrinkReach != null)
            {
                playerDrinkReach.Queue(frame, vessel, ResolveServedCounterWorldPose(vessel), counterSeatView);
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
            if (IsPatronServiceActive)
                return new Pose(patronVesselDock.position + patronCupOfferOffset, patronVesselDock.rotation);
            BarDrinkServicePose localCounter =
                ResolveActiveServiceLocalPose(
                    servicePlan.VesselCounterPose);
            Pose counter = new Pose(
                serviceView.transform.TransformPoint(
                    localCounter.Position + Vector3.forward * ServedVesselServerwardOffset),
                serviceView.transform.rotation * localCounter.Rotation);
            Pose positioned = playerDrinkRigConfigured && vessel != null &&
                   vessel.TryResolveRightHandUprightPose(
                       counter.position,
                       playerDrinkOwnerRoot,
                       out Pose rightHandled)
                ? rightHandled
                : counter;
            // Keep the shared dock while fitting the actual scaled shell on
            // the measured counter. The menu has its separate rear dock;
            // preparation poses never use this path.
            Bounds top = serviceView.CounterSurfaceWorldBounds;
            if (vessel != null && top.size.sqrMagnitude > .0001f)
            {
                Vector3 forward = serviceView.transform.forward;
                Vector3 right = serviceView.transform.right;
                float front = float.PositiveInfinity;
                float back = float.NegativeInfinity;
                float left = float.PositiveInfinity;
                float rightEdge = float.NegativeInfinity;
                float shellFront = float.PositiveInfinity;
                float shellBack = float.NegativeInfinity;
                float shellLeft = float.PositiveInfinity;
                float shellRight = float.NegativeInfinity;
                Bounds shell = vessel.LocalVisualBounds;
                Vector3 scale = vessel.transform.lossyScale;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 signs = new Vector3((corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                    Vector3 surface = top.center + Vector3.Scale(top.extents, signs);
                    front = Mathf.Min(front, Vector3.Dot(surface, forward));
                    back = Mathf.Max(back, Vector3.Dot(surface, forward));
                    left = Mathf.Min(left, Vector3.Dot(surface, right));
                    rightEdge = Mathf.Max(rightEdge, Vector3.Dot(surface, right));
                    Vector3 offset = positioned.rotation * Vector3.Scale(
                        shell.center + Vector3.Scale(shell.extents, signs), scale);
                    shellFront = Mathf.Min(shellFront, Vector3.Dot(offset, forward));
                    shellBack = Mathf.Max(shellBack, Vector3.Dot(offset, forward));
                    shellLeft = Mathf.Min(shellLeft, Vector3.Dot(offset, right));
                    shellRight = Mathf.Max(shellRight, Vector3.Dot(offset, right));
                }
                float depth = Vector3.Dot(positioned.position, forward);
                float supportedDepth = Mathf.Clamp(depth,
                    front + ServedVesselGuestLipClearance - shellFront,
                    back - ServedVesselGuestLipClearance - shellBack);
                positioned.position += forward * (supportedDepth - depth);
                float lateral = Vector3.Dot(positioned.position, right);
                float supportedLateral = Mathf.Clamp(lateral,
                    left + ServedVesselSideClearance - shellLeft,
                    rightEdge - ServedVesselSideClearance - shellRight);
                positioned.position += right * (supportedLateral - lateral);
            }
            return ResolveSupportedVesselPose(vessel, positioned);
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
            bool preservePatronService = IsPatronServiceActive;
            if (preservePatronService) ClearPreservedHeroService();
            heroServiceQueued = menuOrderRequestPending = menuOrderGestureStarted = false;
            pendingHeroPresentation = false;
            CancelRepeatRequest();
            lastServedDrinkId = DrinkId.None;
            if (!preservePatronService) currentOrderDrinkId = DrinkId.None;
            playerDrinkReach?.ResetPresentation();
            hoveredBottle = null;
            counterSeatView?.EndMenuFocus();
            counterMenuHint?.Hide();
            counterMenuModel?.Reset();
            menuDeliveryElapsedSeconds = 0f;
            menuPlacementStarted = false;
            pendingCounterMenuDelivery = false;
            pendingCounterMenuRetrieval = false;
            pendingOrderedDrinkPresentation = false;
            counterServerAtTarget = false;
            returningCounterMenuHome = false;
            restingMenuGazeArmed = false;
            menuPresentation?.ResetPresentation();
            if (!preservePatronService) serviceView?.ResetPresentation();
            if (!preservePatronService)
            {
                activeBottleHandTarget = Vector3.zero;
                activeBottleReachCorrection = Vector3.zero;
                activeBottleHandRadial = Vector3.right;
                activeBottleWorldRotation = Quaternion.identity;
                beerPlacementCaptured = false;
                bottleGripAttached = false;
                bottleReturnCaptured = false;
                emptyVesselStowCaptured = false;
                emptyVesselCounterPose = emptyVesselStowStartPose = default;
                emptyVesselStorageServerPose = default;
                emptyVesselRotationInServer = Quaternion.identity;
                bottleReturnHandStart = Vector3.zero;
                bottleShelfReturnPosition = Vector3.zero;
                bottleShelfReturnRotation = Quaternion.identity;
            }
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
            if (!preservePatronService) ResetCueState();
            Physics.SyncTransforms();

            // Release the selected stool only after its camera/menu state has
            // been restored. The bartender choreography then walks back to
            // its authored home point while the next station remains free to
            // configure a different service offset.
            counterSeatView = null;
            if (!preservePatronService)
            {
                activeServiceLocalOffset = Vector3.zero;
                activeServiceMirrored = false;
            }
            serviceQueue.Cancel(this);
        }

        private void CompleteOrderPresentation()
        {
            playerDrinkReach?.ResetPresentation();
            lastServedDrinkId = CurrentOrderDrinkId;
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
                bool alreadyPlayed = heroConcurrentGulpPlayed;
                heroConcurrentGulpPlayed = true;
                if (!alreadyPlayed || !UsesPhysicalMenu) RetroAudio.Play(RetroSfxId.DrinkGulp);
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
