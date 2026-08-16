using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The bartender's hands during drink service. The authored
    /// <see cref="BarDrinkServiceTimeline"/> channels keep driving the
    /// props exactly as before — his arms are readers, never drivers:
    /// every frame the assigned chain CCD-follows the prop the
    /// timeline moves, so the brass-banded mid-right arm visibly
    /// carries the bottle the hero picked, the mid-left arm rides the
    /// vessel it slides in along the counter, and the lower pair
    /// fingers whatever bottle the hero is only hovering over.
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

        private BarBartenderPresentation presentation;
        private BarDrinkShopController shop;
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
            isInitialized = true;
        }

        private void LateUpdate()
        {
            if (!isInitialized || !presentation.IsInitialized)
            {
                return;
            }

            if (!shop.IsOpen ||
                shop.Timeline == null ||
                shop.ServiceView == null)
            {
                ReleaseAll();
                return;
            }

            BarDrinkServiceFrame frame = shop.Timeline.CurrentFrame;
            ApplyHoverTouch(frame);
            ApplyBottleCarry(frame);
            ApplyVesselGuide(frame);
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
            BarDrinkBottleView bottle =
                shop.ServiceView.SelectedBottle;
            bool inFlight =
                frame.Phase == BarDrinkServicePhase.BottlePickup ||
                frame.Phase == BarDrinkServicePhase.VesselPlacement ||
                frame.Phase == BarDrinkServicePhase.Pouring ||
                frame.Phase == BarDrinkServicePhase.BottleReturn;
            if (bottle == null ||
                !inFlight ||
                !shop.Timeline.IsCommitted)
            {
                presentation.SetChainTarget(
                    BottleChainIndex, Vector3.zero, 0f);
                return;
            }

            presentation.SetChainTarget(
                BottleChainIndex,
                bottle.transform.position +
                bottle.transform.up * 0.30f,
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
}
