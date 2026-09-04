using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The bartender's hands during drink service. The authored
    /// <see cref="BarDrinkServiceTimeline"/> remains the single clock and
    /// keeps driving every prop. The active ordinary bartender reads its
    /// phase into waiter clips while his right hand follows the selected
    /// bottle and his left hand steadies the vessel. The retained legacy
    /// path keeps its former four-chain mapping for prefab inspection.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(315)]
    public sealed class BarBartenderServiceChoreography : MonoBehaviour
    {
        /// <summary>Arm2.L — walks the vessel along the counter.</summary>
        public const int VesselChainIndex = 0;

        /// <summary>Arm2.R — the brass-banded pouring arm.</summary>
        public const int BottleChainIndex = 1;

        public const int LeftTouchChainIndex = 2;
        public const int RightTouchChainIndex = 3;

        public const float TouchWeight = 0.65f;
        public const float CarryWeight = 1f;
        public const float CounterTravelSpeed = 2.25f;

        private BarBartenderPresentation presentation;
        private BarDrinkShopController shop;
        private Vector3 homeLocalPosition;
        private float counterTravelElapsedSeconds;
        private bool isInitialized;

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
            if (presentation.UsesOrdinaryRig)
            {
                BarBartenderAssetRegistry registry = presentation.Registry;
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
            }

            homeLocalPosition = transform.localPosition;
            isInitialized = true;
        }

        private void LateUpdate()
        {
            if (!isInitialized || !presentation.IsInitialized)
            {
                return;
            }

            bool movingAlongCounter = UpdateCounterPosition(Time.deltaTime);
            if (shop.IsReturningCounterMenuHome)
            {
                ReleaseAll();
                if (movingAlongCounter)
                {
                    presentation.ApplyCounterTravelPose(
                        counterTravelElapsedSeconds,
                        leftHandCarriesMenu: true);
                }
                else
                {
                    shop.CompleteCounterMenuReturnHome();
                    presentation.ResetServicePose();
                }

                return;
            }

            if (!shop.IsOpen ||
                shop.Timeline == null ||
                shop.ServiceView == null)
            {
                ReleaseAll();
                if (movingAlongCounter)
                {
                    presentation.ApplyCounterTravelPose(
                        counterTravelElapsedSeconds);
                }
                else
                {
                    presentation.ResetServicePose();
                }
                return;
            }

            BarDrinkServiceFrame frame = shop.Timeline.CurrentFrame;
            BarDrinkMenuPresentation menu = shop.MenuPresentation;
            bool menuHandled =
                menu != null &&
                menu.IsVisible &&
                !menu.IsPlaced &&
                menu.GripAnchor != null;
            presentation.ApplyServiceFrame(frame, menuHandled);
            if (presentation.UsesOrdinaryRig)
            {
                ApplyOrdinaryService(frame, menu, menuHandled);
                if (movingAlongCounter &&
                    shop.MenuState ==
                        BarPromenade.Runtime.World.CounterMenuState.Delivering)
                {
                    presentation.ApplyCounterTravelPose(
                        counterTravelElapsedSeconds,
                        leftHandCarriesMenu: true);
                }
                return;
            }

            ApplyHoverTouch(frame);
            ApplyBottleCarry(frame);
            ApplyVesselGuide(frame);
        }

        private bool UpdateCounterPosition(float deltaTime)
        {
            Vector3 target = homeLocalPosition;
            bool targetsCounter = shop != null &&
                shop.HasCounterServiceTarget;
            if (targetsCounter)
            {
                target = shop.ResolveActiveServiceLocalPosition(
                    homeLocalPosition);
            }

            float step = Mathf.Max(0f, deltaTime) * CounterTravelSpeed;
            Vector3 previous = transform.localPosition;
            transform.localPosition = Vector3.MoveTowards(
                previous,
                target,
                step);
            bool moving = (transform.localPosition - target).sqrMagnitude >
                          0.000001f;
            bool movedThisFrame =
                (transform.localPosition - previous).sqrMagnitude >
                0.000001f;
            if (moving || movedThisFrame)
            {
                counterTravelElapsedSeconds += Mathf.Max(0f, deltaTime);
            }
            else
            {
                counterTravelElapsedSeconds = 0f;
            }

            shop?.ReportCounterServerAtTarget(
                targetsCounter && !moving && !movedThisFrame);

            return moving || movedThisFrame;
        }

        private void ApplyOrdinaryService(
            BarDrinkServiceFrame frame,
            BarDrinkMenuPresentation menu,
            bool menuHandled)
        {
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

        /// <summary>
        /// While the hero browses, the lower pair fingers the hovered
        /// bottle on its shelf — reaching back over his shoulder with
        /// whichever hand is on the bottle's side.
        /// </summary>
        private void ApplyHoverTouch(BarDrinkServiceFrame frame)
        {
            BarDrinkBottleView hovered =
                frame.Phase == BarDrinkServicePhase.Browsing
                    ? shop.HoveredBottle
                    : null;
            if (hovered == null)
            {
                presentation.SetChainTarget(
                    LeftTouchChainIndex, Vector3.zero, 0f);
                presentation.SetChainTarget(
                    RightTouchChainIndex, Vector3.zero, 0f);
                return;
            }

            Vector3 local = transform.InverseTransformPoint(
                hovered.transform.position);
            int chain = local.x >= 0f
                ? LeftTouchChainIndex
                : RightTouchChainIndex;
            int idle = chain == LeftTouchChainIndex
                ? RightTouchChainIndex
                : LeftTouchChainIndex;
            presentation.SetChainTarget(
                chain,
                hovered.transform.position +
                hovered.transform.up * 0.30f,
                TouchWeight);
            presentation.SetChainTarget(idle, Vector3.zero, 0f);
        }

        /// <summary>
        /// The committed bottle rides the timeline from the shelf to
        /// the pour pose; the brass-banded arm holds its body all the
        /// way there and back.
        /// </summary>
        private void ApplyBottleCarry(BarDrinkServiceFrame frame)
        {
            bool bottleHandled =
                frame.Phase == BarDrinkServicePhase.BottlePickup ||
                frame.Phase == BarDrinkServicePhase.VesselPlacement ||
                frame.Phase == BarDrinkServicePhase.Pouring ||
                frame.Phase == BarDrinkServicePhase.BottleReturn ||
                frame.Phase == BarDrinkServicePhase.Drinking ||
                frame.Phase == BarDrinkServicePhase.VesselReturn;
            if (!shop.ServiceView.IsCarriedBottleVisible ||
                !bottleHandled ||
                !shop.Timeline.IsCommitted)
            {
                presentation.SetChainTarget(
                    BottleChainIndex, Vector3.zero, 0f);
                return;
            }

            presentation.SetChainTarget(
                BottleChainIndex,
                shop.ActiveBottleHandTarget,
                CarryWeight);
        }

        /// <summary>
        /// The vessel enters sliding along the counter and stays
        /// steadied until the hero lifts it to drink.
        /// </summary>
        private void ApplyVesselGuide(BarDrinkServiceFrame frame)
        {
            BarDrinkVesselView vessel =
                shop.ServiceView.ActiveVessel;
            bool guided =
                frame.Phase ==
                    BarDrinkServicePhase.VesselPlacement ||
                frame.Phase == BarDrinkServicePhase.Pouring ||
                frame.Phase == BarDrinkServicePhase.BottleReturn;
            if (vessel == null ||
                !guided ||
                !vessel.gameObject.activeInHierarchy)
            {
                presentation.SetChainTarget(
                    VesselChainIndex, Vector3.zero, 0f);
                return;
            }

            presentation.SetChainTarget(
                VesselChainIndex,
                vessel.transform.position + Vector3.up * 0.08f,
                CarryWeight);
        }

        private void ReleaseAll()
        {
            for (int chain = 0;
                 chain < presentation.ChainCount;
                 chain++)
            {
                presentation.SetChainTarget(chain, Vector3.zero, 0f);
            }
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
        private Quaternion bottleSocketRotationInHand;
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
            bottleSocketRotationInHand = Quaternion.Inverse(
                registry.RightHand.rotation) *
                registry.RightBottleSocket.rotation;
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
                !service.IsCarriedBottleVisible)
            {
                return;
            }

            Vector3 bottleUp =
                shop.ActiveBottleWorldRotation * Vector3.up;
            Vector3 radial = Vector3.ProjectOnPlane(
                shop.ActiveBottleHandRadial,
                bottleUp);
            if (radial.sqrMagnitude < 0.000001f)
            {
                radial = Vector3.ProjectOnPlane(
                    transform.right,
                    bottleUp);
            }

            radial.Normalize();
            Vector3 socketForward = Vector3.Cross(radial, bottleUp);
            if (socketForward.sqrMagnitude < 0.000001f)
            {
                return;
            }

            Quaternion socketRotation = Quaternion.LookRotation(
                socketForward.normalized,
                -bottleUp.normalized);
            registry.RightHand.rotation =
                socketRotation *
                Quaternion.Inverse(bottleSocketRotationInHand);
            service.AlignCarriedBottleToCarrier(
                registry.BottleGripAnchor,
                shop.ActiveBottleWorldRotation,
                registry.RightUpperArm.position);
        }
    }
}
