using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Reads the shared service clock into the default bartender's movement and contacts.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(315)]
    public sealed class BarBartenderServiceChoreography : MonoBehaviour
    {
        public const int VesselChainIndex = 0;
        public const int BottleChainIndex = 1;
        public const float CarryWeight = 1f;
        public const float CounterTravelSpeed = 2.25f;
        public const float CounterTurnSpeedDegrees = 360f;
        public const float CounterTravelFacingToleranceDegrees = 8f;
        private const float EmptyVesselCarrySpeed = .8f;

        private const float PositionToleranceSquared = 0.000001f;
        private const float AuthoredFacingToleranceDegrees = 0.1f;

        private BarBartenderPresentation presentation;
        private BarDrinkShopController shop;
        private Vector3 homeLocalPosition;
        private Quaternion homeLocalRotation;
        private Vector3 vesselCarryTargetLocalPosition;
        private Vector3 vesselCarryStartWorldPosition;
        private BarDrinkServicePhase vesselCarryPhase = BarDrinkServicePhase.Closed;
        private const float VesselCarryHandoffSeconds = 0.28f;
        private float counterTravelElapsedSeconds;
        private bool isInitialized;
        private BarDrinkServicePhase emptyReachPhase;
        private Vector3 emptyReachStartWorldPosition;
        private Vector3 emptySupportPalmWorldPosition;

        public bool IsInitialized => isInitialized;

        public void Initialize(
            BarBartenderPresentation bartenderPresentation,
            BarDrinkShopController shopController)
        {
            if (isInitialized)
            {
                throw new InvalidOperationException(
                    "The bartender service choreography is already " +
                    "initialized.");
            }

            presentation = bartenderPresentation != null
                ? bartenderPresentation
                : throw new ArgumentNullException(
                    nameof(bartenderPresentation));
            shop = shopController != null
                ? shopController
                : throw new ArgumentNullException(
                    nameof(shopController));
            BarBartenderAssetRegistry registry = presentation.Registry;
            shop.ConfigureBartenderPresentation(presentation);
            shop.ConfigureBottleReachChain(
                transform,
                registry.RightUpperArm,
                registry.RightForearm,
                registry.RightHand,
                registry.BottleGripAnchor);
            BarBartenderBottleGripPostSolve postSolve =
                GetComponent<BarBartenderBottleGripPostSolve>();
            if (postSolve == null)
            {
                postSolve = gameObject.AddComponent<
                    BarBartenderBottleGripPostSolve>();
            }

            postSolve.Initialize(registry, shop);

            BarBartenderVesselGripPostSolve vesselPostSolve =
                GetComponent<BarBartenderVesselGripPostSolve>();
            if (vesselPostSolve == null)
            {
                vesselPostSolve = gameObject.AddComponent<
                    BarBartenderVesselGripPostSolve>();
            }

            vesselPostSolve.Initialize(registry, shop);
            vesselCarryTargetLocalPosition =
                transform.InverseTransformPoint(
                    registry.VesselGripAnchor.position);

            homeLocalPosition = transform.localPosition;
            homeLocalRotation = transform.localRotation;
            isInitialized = true;
        }

        private void LateUpdate()
        {
            if (!isInitialized || !presentation.IsInitialized)
            {
                return;
            }

            presentation.SetCounterHandSupport(Vector3.zero, 0f);

            bool awayFromHome = (transform.localPosition - homeLocalPosition).sqrMagnitude > PositionToleranceSquared ||
                Quaternion.Angle(transform.localRotation, homeLocalRotation) > AuthoredFacingToleranceDegrees;
            bool serviceRequested = shop.WantsBartenderService || shop.IsReturningCounterMenuHome || awayFromHome;
            presentation.RequestTowelService(serviceRequested);
            if (serviceRequested ? !presentation.IsTowelServiceReady : !presentation.IsTowelWipeReady)
            {
                ReleaseAll();
                return;
            }

            CounterTravelFrame counterTravel =
                UpdateCounterPosition(Time.deltaTime);
            shop.RefreshEmptyVesselForServerPose();
            if (shop.IsReturningCounterMenuHome)
            {
                ReleaseAll();
                if (counterTravel.IsActive)
                {
                    ApplyCounterMotionPose(
                        counterTravel,
                        leftHandOccupied: true);
                }
                else
                {
                    shop.CompleteCounterMenuReturnHome();
                    presentation.RequestTowelService(false);
                    if (presentation.IsTowelWipeReady)
                        presentation.ResetServicePose();
                }

                return;
            }

            if (!shop.WantsBartenderService ||
                shop.Timeline == null ||
                shop.ServiceView == null)
            {
                ReleaseAll();
                if (counterTravel.IsActive)
                {
                    ApplyCounterMotionPose(counterTravel);
                }
                else
                {
                    presentation.ResetServicePose();
                }
                return;
            }

            BarDrinkServiceFrame frame = shop.Timeline.CurrentFrame;
            if (frame.Phase != BarDrinkServicePhase.BeerCarryToGuest &&
                frame.Phase != BarDrinkServicePhase.BottleCarryToGuest)
                vesselCarryPhase = BarDrinkServicePhase.Closed;
            BarDrinkMenuPresentation menu = shop.MenuPresentation;
            bool menuHandled =
                menu != null &&
                menu.IsVisible &&
                !menu.IsPlaced &&
                menu.GripAnchor != null;
            presentation.ApplyServiceFrame(frame, menuHandled);
            ApplyOrdinaryService(frame, menu, menuHandled);
            if (counterTravel.IsActive)
            {
                ApplyCounterMotionPose(counterTravel, IsLeftHandOccupied(frame, menuHandled));
            }
        }

        private CounterTravelFrame UpdateCounterPosition(float deltaTime)
        {
            Vector3 target = homeLocalPosition;
            Quaternion targetRotation = homeLocalRotation;
            BarDrinkServicePhase phase = shop != null
                ? shop.Phase
                : BarDrinkServicePhase.Closed;
            bool targetsBeerTap =
                phase == BarDrinkServicePhase.BeerWalkToTap ||
                phase == BarDrinkServicePhase.BeerGlassPickup ||
                phase == BarDrinkServicePhase.BeerPouring;
            bool targetsBeerGuest =
                phase == BarDrinkServicePhase.BeerCarryToGuest ||
                phase == BarDrinkServicePhase.BeerGlassPlacement;
            bool targetsBottleShelf =
                phase == BarDrinkServicePhase.BottleWalkToShelf ||
                phase == BarDrinkServicePhase.BottlePickup ||
                phase ==
                    BarDrinkServicePhase.BottleWalkToShelfReturn ||
                phase == BarDrinkServicePhase.BottleReturn;
            bool targetsBottlePreparation =
                phase == BarDrinkServicePhase.BottleCarryToPour ||
                phase == BarDrinkServicePhase.VesselPlacement ||
                phase == BarDrinkServicePhase.Pouring;
            bool targetsBottleGuest =
                phase == BarDrinkServicePhase.BottleCarryToGuest ||
                phase ==
                    BarDrinkServicePhase.BottleVesselPlacement;
            bool targetsEmptyGuest =
                phase == BarDrinkServicePhase.EmptyVesselWalkToGuest ||
                phase == BarDrinkServicePhase.EmptyVesselPickup;
            bool targetsEmptyStorage =
                phase == BarDrinkServicePhase.EmptyVesselCarryToStorage ||
                phase == BarDrinkServicePhase.EmptyVesselStow;
            bool waitsAwayFromGuest =
                phase == BarDrinkServicePhase.AwaitingDrink ||
                phase == BarDrinkServicePhase.PlayerPickup ||
                phase == BarDrinkServicePhase.PlayerDrinking ||
                phase == BarDrinkServicePhase.PlayerVesselReturn ||
                phase == BarDrinkServicePhase.EmptyOnCounter;
            bool targetsCounter = shop != null &&
                shop.HasCounterServiceTarget &&
                shop.WantsBartenderService &&
                !targetsBeerTap && !targetsBeerGuest &&
                !targetsBottleShelf && !targetsBottlePreparation &&
                !targetsBottleGuest &&
                !targetsEmptyGuest && !targetsEmptyStorage &&
                !waitsAwayFromGuest;
            if (targetsBeerTap && shop.ServiceView != null &&
                shop.ServiceView.HasBeerTapPresentation)
            {
                Pose tapPose = shop.ServiceView.BeerTapServerWorldPose;
                Transform parent = transform.parent;
                target = parent != null
                    ? parent.InverseTransformPoint(tapPose.position)
                    : tapPose.position;
                targetRotation = parent != null
                    ? Quaternion.Inverse(parent.rotation) * tapPose.rotation
                    : tapPose.rotation;
            }
            else if (targetsBeerGuest || targetsEmptyGuest)
            {
                target = shop.ResolveBeerGuestServerLocalPosition(
                    homeLocalPosition);
            }
            else if (targetsBottleShelf)
            {
                ResolveWorldTarget(
                    shop.ResolveBottleShelfServerWorldPose(),
                    ref target,
                    ref targetRotation);
            }
            else if (targetsBottlePreparation)
            {
                ResolveWorldTarget(
                    shop.ResolveBottlePreparationServerWorldPose(),
                    ref target,
                    ref targetRotation);
            }
            else if (targetsBottleGuest)
            {
                target = shop.ResolveBeerGuestServerLocalPosition(
                    homeLocalPosition);
            }
            else if (targetsEmptyStorage)
            {
                ResolveWorldTarget(shop.ResolveEmptyVesselStorageServerWorldPose(), ref target, ref targetRotation);
            }
            else if (targetsCounter)
            {
                target = shop.ResolveActiveServiceLocalPosition(
                    homeLocalPosition);
            }

            // Retreat while still facing the guest; the occupied hand keeps
            // the same side of the body instead of sweeping through a turn.
            bool carriesEmpty = phase == BarDrinkServicePhase.EmptyVesselCarryToStorage;
            float step = Mathf.Max(0f, deltaTime) *
                (carriesEmpty ? EmptyVesselCarrySpeed : CounterTravelSpeed);
            float turnStep = Mathf.Max(0f, deltaTime) * CounterTurnSpeedDegrees;
            Vector3 previous = transform.localPosition;
            Quaternion previousRotation = transform.localRotation;
            Vector3 flatPath = target - previous;
            flatPath.y = 0f;
            bool hasFlatPath =
                flatPath.sqrMagnitude > PositionToleranceSquared;
            if (carriesEmpty)
            {
                transform.localPosition = Vector3.MoveTowards(previous, target, step);
                transform.localRotation = targetRotation;
            }
            else if (hasFlatPath)
            {
                Quaternion travelRotation = Quaternion.LookRotation(
                    flatPath.normalized,
                    Vector3.up);
                transform.localRotation = Quaternion.RotateTowards(
                    previousRotation,
                    travelRotation,
                    turnStep);
                if (Quaternion.Angle(
                        transform.localRotation,
                        travelRotation) <=
                    CounterTravelFacingToleranceDegrees)
                {
                    transform.localPosition = Vector3.MoveTowards(
                        previous,
                        target,
                        step);
                }
            }
            else
            {
                transform.localPosition = Vector3.MoveTowards(
                    previous,
                    target,
                    step);
                transform.localRotation = Quaternion.RotateTowards(previousRotation, targetRotation, turnStep);
            }

            bool movingPosition =
                (transform.localPosition - target).sqrMagnitude >
                PositionToleranceSquared;
            bool turning = Quaternion.Angle(
                transform.localRotation,
                targetRotation) > AuthoredFacingToleranceDegrees;
            bool translatedThisFrame =
                (transform.localPosition - previous).sqrMagnitude >
                PositionToleranceSquared;
            bool rotatedThisFrame = Quaternion.Angle(
                    previousRotation,
                    transform.localRotation) > 0.01f;
            bool movedThisFrame =
                translatedThisFrame || rotatedThisFrame;
            if (movingPosition || turning || movedThisFrame)
            {
                counterTravelElapsedSeconds += Mathf.Max(0f, deltaTime);
            }
            else
            {
                counterTravelElapsedSeconds = 0f;
            }

            bool arrived = !movingPosition && !turning;
            shop?.ReportCounterServerAtTarget(
                targetsCounter && arrived);
            if (shop != null)
            {
                shop.ReportEmptyVesselServerAtGuest(
                    phase == BarDrinkServicePhase.EmptyVesselWalkToGuest && arrived);
                shop.ReportEmptyVesselServerAtStorage(
                    phase == BarDrinkServicePhase.EmptyVesselCarryToStorage && arrived);
                shop.ReportBeerServerAtTap(
                    phase == BarDrinkServicePhase.BeerWalkToTap &&
                    arrived);
                shop.ReportBeerServerAtGuest(
                    phase == BarDrinkServicePhase.BeerCarryToGuest &&
                    arrived);
                shop.ReportBottleServerAtShelf(
                    phase ==
                        BarDrinkServicePhase.BottleWalkToShelf &&
                    arrived);
                shop.ReportBottleServerAtPour(
                    phase ==
                        BarDrinkServicePhase.BottleCarryToPour &&
                    arrived);
                shop.ReportBottleServerAtGuest(
                    phase ==
                        BarDrinkServicePhase.BottleCarryToGuest &&
                    arrived);
                shop.ReportBottleServerAtShelfReturn(
                    phase ==
                        BarDrinkServicePhase.BottleWalkToShelfReturn &&
                    arrived);
            }

            return new CounterTravelFrame(
                !arrived || movedThisFrame,
                translatedThisFrame);
        }

        private void ResolveWorldTarget(
            Pose worldPose,
            ref Vector3 localPosition,
            ref Quaternion localRotation)
        {
            Transform parent = transform.parent;
            localPosition = parent != null
                ? parent.InverseTransformPoint(worldPose.position)
                : worldPose.position;
            localPosition.y = homeLocalPosition.y;
            localRotation = parent != null
                ? Quaternion.Inverse(parent.rotation) * worldPose.rotation
                : worldPose.rotation;
        }

        private void ApplyCounterMotionPose(
            CounterTravelFrame frame,
            bool leftHandOccupied = false)
        {
            if (frame.IsTranslating)
            {
                presentation.ApplyCounterTravelPose(
                    counterTravelElapsedSeconds,
                    leftHandOccupied,
                    walkingBackwards: shop.Phase == BarDrinkServicePhase.EmptyVesselCarryToStorage);
                return;
            }

            presentation.ApplyCounterTurnPose(
                counterTravelElapsedSeconds,
                leftHandOccupied);
        }

        private bool IsLeftHandOccupied(
            BarDrinkServiceFrame frame,
            bool menuHandled)
        {
            if (menuHandled ||
                shop.MenuState ==
                    BarPromenade.Runtime.World.CounterMenuState.Delivering ||
                shop.MenuState ==
                    BarPromenade.Runtime.World.CounterMenuState.Retrieving)
            {
                return true;
            }

            return frame.Phase == BarDrinkServicePhase.EmptyVesselWalkToGuest ||
                   frame.Phase ==
                       BarDrinkServicePhase.EmptyVesselPickup ||
                   frame.Phase == BarDrinkServicePhase.EmptyVesselCarryToStorage ||
                   frame.Phase == BarDrinkServicePhase.EmptyVesselStow ||
                   frame.Phase ==
                       BarDrinkServicePhase.VesselPlacement ||
                   frame.Phase == BarDrinkServicePhase.Pouring ||
                   frame.Phase == BarDrinkServicePhase.BottleReturn ||
                   frame.Phase == BarDrinkServicePhase.BeerGlassPickup ||
                   frame.Phase == BarDrinkServicePhase.BeerPouring ||
                   frame.Phase == BarDrinkServicePhase.BeerCarryToGuest ||
                   frame.Phase == BarDrinkServicePhase.BeerGlassPlacement ||
                   frame.Phase ==
                       BarDrinkServicePhase.BottleCarryToGuest ||
                   frame.Phase ==
                       BarDrinkServicePhase.BottleVesselPlacement;
        }

        private void ApplyOrdinaryService(
            BarDrinkServiceFrame frame,
            BarDrinkMenuPresentation menu,
            bool menuHandled)
        {
            presentation.SetCounterReachLean(Vector3.zero, 0f);
            if (shop.IsClearingEmptyVessel)
            {
                ApplyEmptyVesselCleanup(frame);
                return;
            }
            emptyReachPhase = BarDrinkServicePhase.Closed;
            if (shop.Timeline.IsBeerService &&
                IsBeerBartenderServicePhase(frame.Phase))
            {
                ApplyOrdinaryBeerService(frame);
                return;
            }

            if (shop.Timeline.IsBottleService &&
                IsBottleBartenderServicePhase(frame.Phase))
            {
                ApplyOrdinaryBottleService(frame);
                return;
            }

            shop.ServiceView.SetBeerTapBartenderContact(false, 0f, 0f);
            shop.ServiceView.SetBeerTapHandlePull(0f);
            bool bottleHandled =
                frame.Phase == BarDrinkServicePhase.BottlePickup ||
                frame.Phase == BarDrinkServicePhase.VesselPlacement ||
                frame.Phase == BarDrinkServicePhase.Pouring ||
                frame.Phase == BarDrinkServicePhase.BottleReturn ||
                frame.Phase == BarDrinkServicePhase.Drinking ||
                frame.Phase == BarDrinkServicePhase.VesselReturn;
            presentation.SetChainTarget(
                BarBartenderPresentation.OrdinaryBottleHandIndex,
                shop.ServiceView.IsCarriedBottleVisible && bottleHandled
                    ? shop.ActiveBottleHandTarget
                    : Vector3.zero,
                shop.ServiceView.IsCarriedBottleVisible &&
                bottleHandled &&
                shop.Timeline.IsCommitted
                    ? CarryWeight
                    : 0f);

            BarDrinkVesselView vessel =
                shop.ServiceView.ActiveVessel;
            bool vesselHandled =
                frame.Phase ==
                    BarDrinkServicePhase.VesselPlacement ||
                frame.Phase == BarDrinkServicePhase.Pouring ||
                frame.Phase == BarDrinkServicePhase.BottleReturn;
            presentation.SetChainTarget(
                BarBartenderPresentation.OrdinaryVesselHandIndex,
                menuHandled
                    ? menu.GripAnchor.position
                    : vessel != null && vesselHandled
                        ? vessel.transform.position + Vector3.up * 0.08f
                        : Vector3.zero,
                menuHandled ||
                (vessel != null &&
                 vesselHandled &&
                 vessel.gameObject.activeInHierarchy)
                    ? CarryWeight
                    : 0f);
        }

        private void ApplyOrdinaryBottleService(
            BarDrinkServiceFrame frame)
        {
            BarDrinkServiceView service = shop.ServiceView;
            BarDrinkVesselView vessel = service.ActiveVessel;
            service.SetBeerTapBartenderContact(false, 0f, 0f);
            service.SetBeerTapHandlePull(0f);

            bool carriesVessel =
                frame.Phase ==
                    BarDrinkServicePhase.BottleCarryToGuest;
            bool guidesVessel = vessel != null &&
                (frame.Phase == BarDrinkServicePhase.VesselPlacement ||
                 frame.Phase == BarDrinkServicePhase.Pouring ||
                 carriesVessel ||
                 frame.Phase ==
                     BarDrinkServicePhase.BottleVesselPlacement);
            float vesselWeight = guidesVessel ? CarryWeight : 0f;
            service.SetBartenderVesselContact(
                carriesVessel,
                vesselWeight);
            Vector3 vesselTarget = Vector3.zero;
            if (guidesVessel && vessel.gameObject.activeInHierarchy)
            {
                vesselTarget = carriesVessel
                    ? ResolveVesselCarryTarget(frame)
                    : vessel.GripWorldPosition;
            }

            presentation.SetChainTarget(
                BarBartenderPresentation.OrdinaryVesselHandIndex,
                vesselTarget,
                vesselTarget != Vector3.zero ? vesselWeight : 0f);
            if (guidesVessel)
                presentation.SetChainGripFrame(BarBartenderPresentation.OrdinaryVesselHandIndex,
                    vessel.OpeningDirection, -vessel.HandleDirection,
                    NpcHandPose.GripKindForVessel(vessel.Kind));

            float bottleWeight = 0f;
            switch (frame.Phase)
            {
                case BarDrinkServicePhase.BottlePickup:
                    bottleWeight = 1f - Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(
                            BarDrinkShopController
                                .BottleGripAttachProgress,
                            0.82f,
                            frame.PhaseProgress));
                    break;
                case BarDrinkServicePhase.Pouring:
                    bottleWeight = Mathf.Max(
                        frame.BottleTilt,
                        frame.StreamVisibility * 0.65f);
                    break;
                case BarDrinkServicePhase.BottleReturn:
                    bottleWeight = CarryWeight;
                    break;
            }

            bool placesVessel = vessel != null &&
                frame.Phase ==
                    BarDrinkServicePhase.BottleVesselPlacement;
            presentation.SetCounterReachLean(
                placesVessel ? vessel.GripWorldPosition : Vector3.zero,
                placesVessel ? 1f : 0f);
            presentation.SetChainTarget(
                BarBartenderPresentation.OrdinaryBottleHandIndex,
                service.IsCarriedBottleVisible && bottleWeight > 0f
                    ? shop.ActiveBottleHandTarget
                    : Vector3.zero,
                service.IsCarriedBottleVisible &&
                shop.Timeline.IsCommitted
                    ? bottleWeight
                    : 0f);
            if (service.IsCarriedBottleVisible && bottleWeight > 0f)
                presentation.SetChainGripFrame(BarBartenderPresentation.OrdinaryBottleHandIndex,
                    shop.ActiveBottleWorldRotation * Vector3.up, -shop.ActiveBottleHandRadial);
        }

        private void ApplyEmptyVesselCleanup(BarDrinkServiceFrame frame)
        {
            BarDrinkServiceView service = shop.ServiceView;
            BarDrinkVesselView vessel = service.ActiveVessel;
            service.SetBeerTapHandlePull(0f);
            service.SetBeerTapBartenderContact(false, 0f, 0f);
            presentation.SetChainTarget(BarBartenderPresentation.OrdinaryBottleHandIndex, Vector3.zero, 0f);
            if (vessel == null || frame.Phase == BarDrinkServicePhase.EmptyVesselWalkToGuest)
            {
                presentation.SetChainTarget(BarBartenderPresentation.OrdinaryVesselHandIndex, Vector3.zero, 0f);
                return;
            }
            if (emptyReachPhase != frame.Phase)
            {
                emptyReachStartWorldPosition = presentation.Registry.VesselGripAnchor.position;
                emptyReachPhase = frame.Phase;
                if (frame.Phase == BarDrinkServicePhase.EmptyVesselStow)
                {
                    Bounds top = service.CounterSurfaceWorldBounds;
                    Vector3 palm = transform.position + transform.right * .24f;
                    palm.x = Mathf.Clamp(palm.x, top.min.x + .10f, top.max.x - .10f);
                    palm.y = service.CounterSurfaceWorldHeight;
                    palm.z = top.max.z - .07f;
                    emptySupportPalmWorldPosition = palm;
                }
            }
            if (frame.Phase == BarDrinkServicePhase.EmptyVesselStow)
                presentation.SetCounterHandSupport(emptySupportPalmWorldPosition, 1f);
            Vector3 target = vessel.GripWorldPosition;
            if (frame.Phase == BarDrinkServicePhase.EmptyVesselPickup && !shop.Timeline.EmptyVesselGripConfirmed)
            {
                float reach = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(frame.PhaseProgress /
                    BarDrinkServiceTimeline.EmptyVesselPickupContactProgress));
                target = Vector3.Lerp(emptyReachStartWorldPosition, target, reach);
            }
            float lean = frame.Phase == BarDrinkServicePhase.EmptyVesselPickup
                ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(frame.PhaseProgress /
                    BarDrinkServiceTimeline.EmptyVesselPickupContactProgress)) : 1f;
            presentation.SetCounterReachLean(vessel.GripWorldPosition, lean);
            presentation.SetChainTarget(BarBartenderPresentation.OrdinaryVesselHandIndex, target, 1f);
            presentation.SetChainElbowDirection(BarBartenderPresentation.OrdinaryVesselHandIndex,
                -transform.up * .50f - transform.forward * .10f - transform.right * .10f);
            presentation.SetChainGripFrame(BarBartenderPresentation.OrdinaryVesselHandIndex,
                vessel.OpeningDirection, -vessel.HandleDirection,
                NpcHandPose.GripKindForVessel(vessel.Kind));
            service.SetBartenderVesselContact(shop.Timeline.EmptyVesselGripConfirmed, 1f);
        }

        private void ApplyOrdinaryBeerService(BarDrinkServiceFrame frame)
        {
            BarDrinkServiceView service = shop.ServiceView;
            BarDrinkVesselView vessel = service.ActiveVessel;
            bool placesVessel = vessel != null &&
                frame.Phase == BarDrinkServicePhase.BeerGlassPlacement;
            presentation.SetCounterReachLean(
                placesVessel ? vessel.GripWorldPosition : Vector3.zero,
                placesVessel ? 1f : 0f);
            service.SetBeerTapHandlePull(frame.TapHandlePull);
            bool carriesVessel =
                frame.Phase == BarDrinkServicePhase.BeerCarryToGuest;
            float vesselWeight = vessel != null &&
                frame.Phase != BarDrinkServicePhase.BeerWalkToTap
                    ? CarryWeight
                    : 0f;
            float handleWeight =
                frame.Phase == BarDrinkServicePhase.BeerPouring
                    ? Mathf.Max(frame.TapHandlePull, 0.35f)
                    : 0f;
            service.SetBeerTapBartenderContact(
                carriesVessel,
                vesselWeight,
                handleWeight);

            Vector3 vesselTarget = Vector3.zero;
            if (vessel != null && vesselWeight > 0f)
            {
                vesselTarget = carriesVessel
                    ? ResolveVesselCarryTarget(frame)
                    : vessel.GripWorldPosition;
            }

            presentation.SetChainTarget(
                BarBartenderPresentation.OrdinaryVesselHandIndex,
                vesselTarget,
                vesselWeight);
            if (vessel != null && vesselWeight > 0f)
                presentation.SetChainGripFrame(BarBartenderPresentation.OrdinaryVesselHandIndex,
                    vessel.OpeningDirection, -vessel.HandleDirection,
                    NpcHandPose.GripKindForVessel(vessel.Kind));
            presentation.SetChainTarget(
                BarBartenderPresentation.OrdinaryBottleHandIndex,
                service.BeerTapHandleGripWorldPosition,
                handleWeight);
        }

        private static bool IsBeerBartenderServicePhase(
            BarDrinkServicePhase phase)
        {
            return phase == BarDrinkServicePhase.BeerWalkToTap ||
                   phase == BarDrinkServicePhase.BeerGlassPickup ||
                   phase == BarDrinkServicePhase.BeerPouring ||
                   phase == BarDrinkServicePhase.BeerCarryToGuest ||
                   phase == BarDrinkServicePhase.BeerGlassPlacement;
        }

        private Vector3 ResolveVesselCarryTarget(BarDrinkServiceFrame frame)
        {
            if (vesselCarryPhase != frame.Phase)
            {
                vesselCarryStartWorldPosition = shop.ServiceView.ActiveVessel.GripWorldPosition;
                vesselCarryPhase = frame.Phase;
            }
            Vector3 destination = shop.ResolveBartenderVesselCarryTarget(
                transform.TransformPoint(vesselCarryTargetLocalPosition));
            float amount = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(frame.PhaseElapsedSeconds / VesselCarryHandoffSeconds));
            return Vector3.Lerp(vesselCarryStartWorldPosition, destination, amount);
        }

        private static bool IsBottleBartenderServicePhase(
            BarDrinkServicePhase phase)
        {
            return phase == BarDrinkServicePhase.BottleWalkToShelf ||
                   phase == BarDrinkServicePhase.BottlePickup ||
                   phase == BarDrinkServicePhase.BottleCarryToPour ||
                   phase == BarDrinkServicePhase.VesselPlacement ||
                   phase == BarDrinkServicePhase.Pouring ||
                   phase == BarDrinkServicePhase.BottleCarryToGuest ||
                   phase ==
                       BarDrinkServicePhase.BottleVesselPlacement ||
                   phase ==
                       BarDrinkServicePhase.BottleWalkToShelfReturn ||
                   phase == BarDrinkServicePhase.BottleReturn;
        }

        private void ReleaseAll()
        {
            presentation?.SetCounterHandSupport(Vector3.zero, 0f);
            presentation?.SetCounterReachLean(Vector3.zero, 0f);
            shop?.ServiceView?.SetBartenderVesselContact(false, 0f);
            shop?.ServiceView?.SetBeerTapBartenderContact(false, 0f, 0f);
            shop?.ServiceView?.SetBeerTapHandlePull(0f);
            for (int chain = 0;
                 chain < presentation.ChainCount;
                 chain++)
            {
                presentation.SetChainTarget(chain, Vector3.zero, 0f);
            }
        }

        private void OnDisable()
        {
            if (isInitialized && presentation != null) ReleaseAll();
        }

        private readonly struct CounterTravelFrame
        {
            public CounterTravelFrame(
                bool isActive,
                bool isTranslating)
            {
                IsActive = isActive;
                IsTranslating = isTranslating;
            }

            public bool IsActive { get; }
            public bool IsTranslating { get; }
        }
    }

    /// <summary>
    /// Seats the authored glass grip on the ordinary bartender's animated
    /// left-hand carrier after animation and IK have both run. Keeping the
    /// glass under the scale-free service root avoids inherited FBX scale.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(326)]
    internal sealed class BarBartenderVesselGripPostSolve : MonoBehaviour
    {
        private BarBartenderAssetRegistry registry;
        private BarDrinkShopController shop;
        private BarBartenderPresentation presentation;
        private bool initialized;

        public void Initialize(
            BarBartenderAssetRegistry assetRegistry,
            BarDrinkShopController shopController)
        {
            registry = assetRegistry != null
                ? assetRegistry
                : throw new ArgumentNullException(nameof(assetRegistry));
            shop = shopController != null
                ? shopController
                : throw new ArgumentNullException(nameof(shopController));
            presentation = GetComponent<BarBartenderPresentation>();
            initialized = true;
        }

        private void LateUpdate()
        {
            BarDrinkServiceView service = shop != null
                ? shop.ServiceView
                : null;
            if (!initialized || service == null ||
                service.ActiveVessel == null ||
                registry.VesselGripAnchor == null)
            {
                return;
            }

            if (shop.IsClearingEmptyVessel)
            {
                if (shop.Phase == BarDrinkServicePhase.EmptyVesselWalkToGuest) return;
                if (shop.Phase == BarDrinkServicePhase.EmptyVesselPickup &&
                    shop.Timeline.PhaseProgress + .0001f < BarDrinkServiceTimeline.EmptyVesselPickupContactProgress)
                    return;
                BarDrinkVesselView empty = service.ActiveVessel;
                presentation?.ResolveAnatomicalGripContactAfterWristRoll(
                    BarBartenderPresentation.OrdinaryVesselHandIndex, empty.GripWorldPosition,
                    empty.OpeningDirection, -empty.HandleDirection,
                    NpcHandPose.GripKindForVessel(empty.Kind));
                bool contact = Vector3.Distance(registry.VesselGripAnchor.position,
                    empty.GripWorldPosition) <= .012f;
                shop.ReportEmptyVesselGripContact(contact);
                if (shop.Timeline.EmptyVesselGripConfirmed)
                    service.AlignActiveVesselGripPositionTo(registry.VesselGripAnchor, empty.transform.rotation);
                shop.TryBeginEmptyVesselCarry();
                shop.TryCompleteEmptyVesselStorage();
                return;
            }
            if (!service.IsActiveVesselCarriedByBartender) return;

            Pose counter = shop.ResolveServedCounterWorldPose(
                service.ActiveVessel);
            service.AlignActiveVesselGripPositionTo(
                registry.VesselGripAnchor,
                counter.rotation);
        }
    }

    /// <summary>
    /// Runs after the ordinary presentation's CCD pass. Point IK cannot choose
    /// wrist roll, so this final visual pass gives the anatomical right palm a
    /// bottle-relative frame and then re-seats the carried copy on the same
    /// socket. It owns no timeline state and never moves the shelf source.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(325)]
    internal sealed class BarBartenderBottleGripPostSolve : MonoBehaviour
    {
        private BarBartenderAssetRegistry registry;
        private BarDrinkShopController shop;
        private BarBartenderPresentation presentation;
        private bool initialized;

        public void Initialize(
            BarBartenderAssetRegistry assetRegistry,
            BarDrinkShopController shopController)
        {
            registry = assetRegistry != null
                ? assetRegistry
                : throw new ArgumentNullException(nameof(assetRegistry));
            shop = shopController != null
                ? shopController
                : throw new ArgumentNullException(nameof(shopController));
            presentation = GetComponent<BarBartenderPresentation>();
            initialized = true;
        }

        private void LateUpdate()
        {
            BarDrinkServiceView service = shop != null
                ? shop.ServiceView
                : null;
            if (!initialized ||
                !shop.IsServing ||
                service == null ||
                !service.IsCarriedBottleVisible ||
                (shop.Timeline.IsBottleService &&
                 !shop.IsBottleGripAttached))
            {
                return;
            }

            // Rebuild the bottle-side contact after root travel and turning.
            // The previous frame's world-space radial is no longer valid once
            // the bartender has changed orientation.
            service.AlignCarriedBottleToCarrier(
                registry.BottleGripAnchor,
                shop.ActiveBottleWorldRotation,
                registry.RightUpperArm.position);
            Vector3 bottleUp =
                shop.ActiveBottleWorldRotation * Vector3.up;
            Vector3 radial = Vector3.ProjectOnPlane(service.ResolveCarriedBottleHandRadial(
                service.CarriedBottleRoot.position, shop.ActiveBottleWorldRotation,
                registry.RightUpperArm.position), bottleUp);
            if (radial.sqrMagnitude < 0.000001f)
            {
                radial = Vector3.ProjectOnPlane(
                    shop.ActiveBottleHandRadial,
                    bottleUp);
                if (radial.sqrMagnitude < 0.000001f)
                {
                    radial = Vector3.ProjectOnPlane(
                        transform.right,
                        bottleUp);
                }
            }

            radial.Normalize();
            presentation?.ResolveAnatomicalGripContactAfterWristRoll(
                BarBartenderPresentation.OrdinaryBottleHandIndex,
                shop.ActiveBottleHandTarget, bottleUp, -radial);
            service.AlignCarriedBottleToCarrier(
                registry.BottleGripAnchor,
                shop.ActiveBottleWorldRotation,
                registry.RightUpperArm.position);
            shop.RefreshBottlePourStreamAfterGrip();
        }
    }
}
