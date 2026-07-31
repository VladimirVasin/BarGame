using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Runtime facade for the generated counter set. Controllers animate this
    /// component and never need to search generated geometry by object name.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BarDrinkServiceView : MonoBehaviour
    {
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private BarDrinkBottleView[] bottles =
            Array.Empty<BarDrinkBottleView>();
        [SerializeField] private BarDrinkVesselView[] vessels =
            Array.Empty<BarDrinkVesselView>();
        [SerializeField] private Transform streamRoot;
        [SerializeField] private Renderer streamRenderer;

        private BarDrinkServicePlan plan;
        private ReadOnlyCollection<BarDrinkBottleView> bottlesView;
        private ReadOnlyCollection<BarDrinkVesselView> vesselsView;
        private IReadOnlyDictionary<DrinkId, BarDrinkBottleView> bottlesByDrink;
        private IReadOnlyDictionary<BarDrinkVesselKind, BarDrinkVesselView>
            vesselsByKind;
        private MaterialPropertyBlock streamProperties;
        private BarDrinkBottleView selectedBottle;
        private BarDrinkVesselView activeVessel;
        private bool initialized;

        public BarDrinkServicePlan Plan => plan;
        public IReadOnlyList<BarDrinkBottleView> Bottles =>
            bottlesView ??= Array.AsReadOnly(
                bottles ?? Array.Empty<BarDrinkBottleView>());
        public IReadOnlyList<BarDrinkVesselView> Vessels =>
            vesselsView ??= Array.AsReadOnly(
                vessels ?? Array.Empty<BarDrinkVesselView>());
        public BarDrinkBottleView SelectedBottle => selectedBottle;
        public DrinkId SelectedDrinkId =>
            selectedBottle != null ? selectedBottle.DrinkId : DrinkId.None;
        public BarDrinkVesselView ActiveVessel => activeVessel;
        public Transform StreamRoot => streamRoot;
        public Renderer StreamRenderer => streamRenderer;
        public Transform ReferenceTransform => transform;
        public bool IsStreamVisible =>
            streamRoot != null && streamRoot.gameObject.activeSelf;

        internal void Initialize(
            BarDrinkServicePlan newPlan,
            IReadOnlyList<BarDrinkBottleView> newBottles,
            IReadOnlyList<BarDrinkVesselView> newVessels,
            Transform newStreamRoot,
            Renderer newStreamRenderer)
        {
            plan = newPlan ?? throw new ArgumentNullException(nameof(newPlan));
            if (newBottles == null ||
                newBottles.Count != BarDrinkServicePlan.RequiredBottleCount)
            {
                throw new ArgumentException(
                    "Service view requires exactly nine bottle views.",
                    nameof(newBottles));
            }

            if (newVessels == null || newVessels.Count != 5)
            {
                throw new ArgumentException(
                    "Service view requires all five vessel variants.",
                    nameof(newVessels));
            }

            var bottleCopy = new BarDrinkBottleView[newBottles.Count];
            var bottleLookup = new Dictionary<DrinkId, BarDrinkBottleView>();
            for (int index = 0; index < newBottles.Count; index++)
            {
                BarDrinkBottleView bottle = newBottles[index];
                if (bottle == null ||
                    !bottle.transform.IsChildOf(transform) ||
                    !bottleLookup.TryAdd(bottle.DrinkId, bottle))
                {
                    throw new ArgumentException(
                        "Bottle views must be unique children of the service root.",
                        nameof(newBottles));
                }

                bottleCopy[index] = bottle;
            }

            var vesselCopy = new BarDrinkVesselView[newVessels.Count];
            var vesselLookup =
                new Dictionary<BarDrinkVesselKind, BarDrinkVesselView>();
            for (int index = 0; index < newVessels.Count; index++)
            {
                BarDrinkVesselView vessel = newVessels[index];
                if (vessel == null ||
                    !vessel.transform.IsChildOf(transform) ||
                    !vesselLookup.TryAdd(vessel.Kind, vessel))
                {
                    throw new ArgumentException(
                        "Vessel views must be unique children of the service root.",
                        nameof(newVessels));
                }

                vesselCopy[index] = vessel;
            }

            if (newStreamRoot == null ||
                !newStreamRoot.IsChildOf(transform) ||
                newStreamRenderer == null ||
                !newStreamRenderer.transform.IsChildOf(newStreamRoot))
            {
                throw new ArgumentException(
                    "The stream must belong to the service root.",
                    nameof(newStreamRoot));
            }

            bottles = bottleCopy;
            vessels = vesselCopy;
            bottlesView = Array.AsReadOnly(bottleCopy);
            vesselsView = Array.AsReadOnly(vesselCopy);
            bottlesByDrink = new ReadOnlyDictionary<DrinkId, BarDrinkBottleView>(
                bottleLookup);
            vesselsByKind =
                new ReadOnlyDictionary<BarDrinkVesselKind, BarDrinkVesselView>(
                    vesselLookup);
            streamRoot = newStreamRoot;
            streamRenderer = newStreamRenderer;
            streamProperties = new MaterialPropertyBlock();
            initialized = true;
            ResetPresentation();
        }

        public bool TryGetBottle(
            DrinkId drinkId,
            out BarDrinkBottleView bottle)
        {
            if (!initialized || drinkId == DrinkId.None)
            {
                bottle = null;
                return false;
            }

            return bottlesByDrink.TryGetValue(drinkId, out bottle);
        }

        public bool TryGetBottle(
            Collider selectionCollider,
            out BarDrinkBottleView bottle)
        {
            bottle = null;
            if (!initialized || selectionCollider == null)
            {
                return false;
            }

            BarDrinkBottleView candidate =
                selectionCollider.GetComponentInParent<BarDrinkBottleView>();
            if (candidate == null ||
                (candidate.SelectionTrigger != selectionCollider &&
                 candidate.SolidCollider != selectionCollider))
            {
                return false;
            }

            return bottlesByDrink.TryGetValue(candidate.DrinkId, out bottle) &&
                   bottle == candidate;
        }

        public bool SelectBottle(DrinkId drinkId)
        {
            if (!TryGetBottle(drinkId, out BarDrinkBottleView next))
            {
                return false;
            }

            if (selectedBottle != null && selectedBottle != next)
            {
                selectedBottle.ResetExact();
            }

            selectedBottle = next;
            selectedBottle.SetShelfPresentation(1f);
            return true;
        }

        public void SetSelectionPresentation(float amount)
        {
            selectedBottle?.SetShelfPresentation(amount);
        }

        public void ClearSelection()
        {
            selectedBottle?.ResetExact();
            selectedBottle = null;
        }

        public void SetSelectedBottleWorldPose(
            Vector3 position,
            Quaternion rotation,
            bool disableColliders = true)
        {
            if (selectedBottle == null)
            {
                return;
            }

            if (disableColliders)
            {
                selectedBottle.SetColliderState(false, false);
            }

            selectedBottle.SetWorldPose(position, rotation);
        }

        public void SetSelectedBottleLocalPose(
            BarDrinkServicePose localPose,
            bool disableColliders = true)
        {
            if (selectedBottle == null)
            {
                return;
            }

            if (disableColliders)
            {
                selectedBottle.SetColliderState(false, false);
            }

            selectedBottle.SetLocalPose(localPose, transform);
        }

        public void SetSelectedBottleAtHand()
        {
            SetSelectedBottleLocalPose(plan.BottleHandPose);
        }

        public void SetSelectedBottleAtPourPose()
        {
            SetSelectedBottleLocalPose(plan.BottlePourPose);
        }

        public void ResetSelectedBottle()
        {
            if (selectedBottle == null)
            {
                return;
            }

            selectedBottle.ResetExact();
            selectedBottle.SetShelfPresentation(1f);
        }

        public bool ShowVesselForDrink(DrinkId drinkId)
        {
            if (!initialized ||
                !BarDrinkPresentationCatalog.TryGet(
                    drinkId,
                    out BarDrinkPresentation presentation) ||
                !vesselsByKind.TryGetValue(
                    presentation.VesselKind,
                    out BarDrinkVesselView vessel))
            {
                return false;
            }

            if (activeVessel != null && activeVessel != vessel)
            {
                activeVessel.ResetExact();
                activeVessel.gameObject.SetActive(false);
            }

            activeVessel = vessel;
            activeVessel.ResetExact();
            activeVessel.gameObject.SetActive(true);
            activeVessel.ConfigureLiquid(
                presentation.LiquidColor,
                presentation.TargetFill);
            SetActiveVesselAtCounter();
            return true;
        }

        public void SetActiveVesselWorldPose(
            Vector3 position,
            Quaternion rotation)
        {
            activeVessel?.SetWorldPose(position, rotation);
        }

        public void SetActiveVesselLocalPose(BarDrinkServicePose localPose)
        {
            activeVessel?.SetLocalPose(localPose, transform);
        }

        public void SetActiveVesselAtCounter()
        {
            SetActiveVesselLocalPose(plan.VesselCounterPose);
        }

        public void SetActiveVesselAtHand()
        {
            SetActiveVesselLocalPose(plan.VesselHandPose);
        }

        public void SetFillProgress(float progress)
        {
            activeVessel?.SetFillProgress(progress);
        }

        public void HideVessel()
        {
            if (activeVessel == null)
            {
                return;
            }

            activeVessel.ResetExact();
            activeVessel.gameObject.SetActive(false);
            activeVessel = null;
        }

        public bool SetPourStreamFromBottle(
            Color color,
            float width = 0.018f)
        {
            if (selectedBottle == null || activeVessel == null)
            {
                HidePourStream();
                return false;
            }

            return SetPourStream(
                selectedBottle.MouthWorldPosition,
                activeVessel.PourTargetWorldPosition,
                color,
                width);
        }

        public bool SetPourStream(
            Vector3 startWorld,
            Vector3 endWorld,
            Color color,
            float width = 0.018f)
        {
            if (!initialized ||
                float.IsNaN(width) ||
                float.IsInfinity(width) ||
                width <= 0f)
            {
                HidePourStream();
                return false;
            }

            Vector3 delta = endWorld - startWorld;
            float length = delta.magnitude;
            if (length < 0.005f)
            {
                HidePourStream();
                return false;
            }

            Color displayed = color;
            displayed.a = Mathf.Clamp(displayed.a, 0.78f, 0.96f);
            streamRenderer.GetPropertyBlock(streamProperties);
            streamProperties.SetColor(BaseColorId, displayed);
            streamProperties.SetColor(ColorId, displayed);
            streamRenderer.SetPropertyBlock(streamProperties);

            streamRoot.gameObject.SetActive(true);
            streamRoot.SetPositionAndRotation(
                Vector3.Lerp(startWorld, endWorld, 0.5f),
                Quaternion.FromToRotation(Vector3.up, delta / length));
            streamRoot.localScale = new Vector3(
                width,
                length * 0.5f,
                width);
            return true;
        }

        public void HidePourStream()
        {
            if (streamRoot != null)
            {
                streamRoot.gameObject.SetActive(false);
            }
        }

        public void ResetPresentation()
        {
            if (!initialized)
            {
                return;
            }

            HidePourStream();
            for (int index = 0; index < bottles.Length; index++)
            {
                bottles[index]?.ResetExact();
            }

            for (int index = 0; index < vessels.Length; index++)
            {
                BarDrinkVesselView vessel = vessels[index];
                if (vessel != null)
                {
                    vessel.ResetExact();
                    vessel.gameObject.SetActive(false);
                }
            }

            selectedBottle = null;
            activeVessel = null;
        }

        private void OnDisable()
        {
            ResetPresentation();
        }
    }
}
