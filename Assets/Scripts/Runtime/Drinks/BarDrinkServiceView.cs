using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    internal readonly struct BarBeerTapRuntimeBinding
    {
        public BarBeerTapRuntimeBinding(
            Transform serverDock,
            Transform vesselDock,
            Transform spout,
            Transform handlePivot,
            Transform handleGrip,
            Transform handleRoot,
            bool isAuthored)
        {
            ServerDock = serverDock;
            VesselDock = vesselDock;
            Spout = spout;
            HandlePivot = handlePivot;
            HandleGrip = handleGrip;
            HandleRoot = handleRoot;
            IsAuthored = isAuthored;
        }

        public Transform ServerDock { get; }
        public Transform VesselDock { get; }
        public Transform Spout { get; }
        public Transform HandlePivot { get; }
        public Transform HandleGrip { get; }
        public Transform HandleRoot { get; }
        public bool IsAuthored { get; }
    }

    /// <summary>
    /// Runtime facade for the generated counter set. Controllers animate this
    /// component and never need to search generated geometry by object name.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BarDrinkServiceView : MonoBehaviour
    {
        public const float CarriedBottleScale = 0.42f;
        public const float CarriedBottleGripHeightShare = 0.55f;
        public const float BottleHandSurfaceOffset = 0.06f;
        public const float MinimumBottleHandRadialClearance = 0.055f;
        public const float BeerTapPourGap = 0.06f;

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private BarDrinkBottleView[] bottles =
            Array.Empty<BarDrinkBottleView>();
        [SerializeField] private BarDrinkVesselView[] vessels =
            Array.Empty<BarDrinkVesselView>();
        [SerializeField] private Transform streamRoot;
        [SerializeField] private Renderer streamRenderer;
        [SerializeField] private BarDrinkMenuPresentation menuPresentation;
        [SerializeField] private Transform beerTapServerDock;
        [SerializeField] private Transform beerTapVesselDock;
        [SerializeField] private Transform beerTapSpout;
        [SerializeField] private Transform beerTapHandlePivot;
        [SerializeField] private Transform beerTapHandleGrip;
        [SerializeField] private Transform beerTapHandleRoot;

        private BarDrinkServicePlan plan;
        private ReadOnlyCollection<BarDrinkBottleView> bottlesView;
        private ReadOnlyCollection<BarDrinkVesselView> vesselsView;
        private IReadOnlyDictionary<DrinkId, BarDrinkBottleView> bottlesByDrink;
        private IReadOnlyDictionary<BarDrinkVesselKind, BarDrinkVesselView>
            vesselsByKind;
        private MaterialPropertyBlock streamProperties;
        private BarDrinkBottleView selectedBottle;
        private BarDrinkVesselView activeVessel;
        private Transform carriedBottleRoot;
        private Transform carriedBottleContact;
        private float carriedBottleHeight;
        private Renderer[] carriedSourceRenderers = Array.Empty<Renderer>();
        private bool[] carriedSourceRendererStates = Array.Empty<bool>();
        private Transform beerTapDynamicHandleGrip;
        private Vector3 beerTapHandleRestLocalPosition;
        private Quaternion beerTapHandleRestLocalRotation =
            Quaternion.identity;
        private Vector3 beerTapHandleGripInPivot;
        private Quaternion beerTapHandleGripRotationInPivot =
            Quaternion.identity;
        private bool hasAuthoredBeerTapPresentation;
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
        public BarDrinkMenuPresentation MenuPresentation =>
            menuPresentation;
        public Transform ReferenceTransform => transform;
        public bool IsStreamVisible =>
            streamRoot != null && streamRoot.gameObject.activeSelf;
        public Transform CarriedBottleRoot => carriedBottleRoot;
        public bool IsCarriedBottleVisible =>
            carriedBottleRoot != null &&
            carriedBottleRoot.gameObject.activeSelf;
        public Vector3 CarriedBottleMouthWorldPosition =>
            IsCarriedBottleVisible
                ? carriedBottleRoot.TransformPoint(
                    Vector3.up * carriedBottleHeight)
                : Vector3.positiveInfinity;
        public Vector3 CarriedBottleGripCenterWorldPosition =>
            IsCarriedBottleVisible
                ? carriedBottleRoot.TransformPoint(
                    Vector3.up *
                    (carriedBottleHeight *
                     CarriedBottleGripHeightShare))
                : Vector3.positiveInfinity;
        public Vector3 CarriedBottleHandContactWorldPosition =>
            IsCarriedBottleVisible && carriedBottleContact != null
                ? carriedBottleContact.position
                : Vector3.positiveInfinity;
        public float CarriedBottleHandRadialClearance =>
            IsCarriedBottleVisible && carriedBottleContact != null
                ? Vector3.ProjectOnPlane(
                    carriedBottleContact.position -
                    CarriedBottleGripCenterWorldPosition,
                    carriedBottleRoot.up).magnitude
                : 0f;
        public bool HasBeerTapPresentation =>
            initialized && beerTapServerDock != null &&
            beerTapVesselDock != null && beerTapSpout != null &&
            beerTapHandlePivot != null && beerTapDynamicHandleGrip != null;
        public bool HasAuthoredBeerTapPresentation =>
            HasBeerTapPresentation && hasAuthoredBeerTapPresentation;
        public Transform BeerTapServerDock => beerTapServerDock;
        public Transform BeerTapVesselDock => beerTapVesselDock;
        public Transform BeerTapSpout => beerTapSpout;
        public Transform BeerTapHandlePivot => beerTapHandlePivot;
        public Transform BeerTapHandleGrip => beerTapHandleGrip;
        public Transform BeerTapHandleRoot => beerTapHandleRoot;
        public Pose BeerTapServerWorldPose => ResolveWorldPose(
            beerTapServerDock,
            plan != null
                ? plan.BeerTap.ServerPose
                : default);
        public Pose BeerTapVesselWorldPose => ResolveWorldPose(
            beerTapVesselDock,
            plan != null
                ? plan.BeerTap.VesselPose
                : default);
        public Pose BeerTapSpoutWorldPose => ResolveWorldPose(
            beerTapSpout,
            plan != null
                ? plan.BeerTap.SpoutPose
                : default);
        public Pose BeerTapHandlePivotWorldPose => ResolveWorldPose(
            beerTapHandlePivot,
            plan != null
                ? plan.BeerTap.HandlePivotPose
                : default);
        public Vector3 BeerTapSpoutWorldPosition =>
            BeerTapSpoutWorldPose.position;
        public Vector3 BeerTapHandleGripWorldPosition =>
            beerTapDynamicHandleGrip != null
                ? beerTapDynamicHandleGrip.position
                : transform.TransformPoint(
                    plan.BeerTap.HandleGripPose.Position);
        public Transform BeerTapHandleGripTarget =>
            beerTapDynamicHandleGrip;
        public float BeerTapHandlePullAmount { get; private set; }
        public bool IsBeerTapVesselCarriedByBartender { get; private set; }
        public float BeerTapVesselHandWeight { get; private set; }
        public float BeerTapHandleHandWeight { get; private set; }

        internal void Initialize(
            BarDrinkServicePlan newPlan,
            IReadOnlyList<BarDrinkBottleView> newBottles,
            IReadOnlyList<BarDrinkVesselView> newVessels,
            Transform newStreamRoot,
            Renderer newStreamRenderer,
            BarDrinkMenuPresentation newMenuPresentation,
            BarBeerTapRuntimeBinding beerTapBinding)
        {
            plan = newPlan ?? throw new ArgumentNullException(nameof(newPlan));
            if (newBottles == null ||
                newBottles.Count != BarDrinkServicePlan.RequiredBottleCount)
            {
                throw new ArgumentException(
                    "Service view requires exactly four bottle views.",
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

            if (newMenuPresentation == null ||
                !newMenuPresentation.IsConfigured ||
                !newMenuPresentation.transform.IsChildOf(transform))
            {
                throw new ArgumentException(
                    "The physical menu must belong to the service root.",
                    nameof(newMenuPresentation));
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
            menuPresentation = newMenuPresentation;
            beerTapServerDock = beerTapBinding.ServerDock;
            beerTapVesselDock = beerTapBinding.VesselDock;
            beerTapSpout = beerTapBinding.Spout;
            beerTapHandlePivot = beerTapBinding.HandlePivot;
            beerTapHandleGrip = beerTapBinding.HandleGrip;
            beerTapHandleRoot = beerTapBinding.HandleRoot;
            hasAuthoredBeerTapPresentation = beerTapBinding.IsAuthored;
            ConfigureBeerTapRuntime();
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

        /// <summary>
        /// Creates a hand-sized, renderer-only copy of the selected shelf
        /// bottle. The physical shelf source never changes transform or
        /// collider state; only its renderers are hidden while the copy is in
        /// the bartender's hand.
        /// </summary>
        public bool ShowCarriedBottle(
            BarDrinkPresentation presentation,
            Transform carrier)
        {
            if (!initialized || selectedBottle == null ||
                presentation.DrinkId != selectedBottle.DrinkId)
            {
                return false;
            }

            HideCarriedBottle();
            var carriedObject = new GameObject(
                $"Carried Bar Bottle {presentation.StableId}");
            carriedBottleRoot = carriedObject.transform;
            // Imported FBX sockets carry a 100x bone hierarchy scale. Keep
            // the visual under the scale-free service root and follow the
            // socket by world pose; parenting the prop to the bone would
            // magnify it even though its contact point was correct.
            carriedBottleRoot.SetParent(transform, false);
            carriedBottleRoot.localScale =
                Vector3.one * CarriedBottleScale;
            if (carrier != null)
            {
                carriedBottleRoot.position = carrier.position;
            }
            carriedBottleHeight =
                BarDrinkServiceWorldBuilder.BuildBottleVisual(
                    carriedBottleRoot,
                    presentation);
            var contactObject = new GameObject(
                "Carried Bottle Hand Contact");
            carriedBottleContact = contactObject.transform;
            carriedBottleContact.SetParent(carriedBottleRoot, false);

            CaptureAndHideCarriedSource();
            carriedBottleRoot.gameObject.SetActive(true);
            return true;
        }

        public void SetCarriedBottleWorldPose(
            Vector3 position,
            Quaternion rotation)
        {
            if (carriedBottleRoot != null)
            {
                carriedBottleRoot.SetPositionAndRotation(position, rotation);
            }
        }

        public void SetCarriedBottleWorldPose(
            Vector3 position,
            Quaternion rotation,
            Vector3 holderWorldPosition)
        {
            SetCarriedBottleWorldPose(position, rotation);
            if (IsCarriedBottleVisible && carriedBottleContact != null)
            {
                carriedBottleContact.position =
                    ResolveCarriedBottleHandContact(
                        position,
                        rotation,
                        holderWorldPosition);
            }
        }

        public void AlignCarriedBottleToCarrier(
            Transform carrier,
            Quaternion bottleWorldRotation,
            Vector3 holderWorldPosition)
        {
            if (!IsCarriedBottleVisible || carrier == null ||
                carriedBottleContact == null)
            {
                return;
            }

            carriedBottleRoot.SetPositionAndRotation(
                carrier.position,
                bottleWorldRotation);
            Vector3 handContact = ResolveCarriedBottleHandContact(
                carriedBottleRoot.position,
                bottleWorldRotation,
                holderWorldPosition);
            carriedBottleContact.position = handContact;
            carriedBottleRoot.position +=
                carrier.position - carriedBottleContact.position;
        }

        public Vector3 ResolveCarriedBottleHandContact()
        {
            return IsCarriedBottleVisible && carriedBottleContact != null
                ? carriedBottleContact.position
                : Vector3.positiveInfinity;
        }

        public Vector3 ResolveCarriedBottleHandContact(
            Vector3 bottleBaseWorldPosition,
            Quaternion bottleWorldRotation,
            Vector3 holderWorldPosition)
        {
            Vector3 bottleUp = bottleWorldRotation * Vector3.up;
            float gripDistance = carriedBottleRoot != null
                ? carriedBottleRoot.TransformVector(
                    Vector3.up *
                    (carriedBottleHeight *
                     CarriedBottleGripHeightShare)).magnitude
                : carriedBottleHeight * CarriedBottleScale *
                  CarriedBottleGripHeightShare;
            Vector3 gripCenter = bottleBaseWorldPosition +
                bottleUp * gripDistance;
            Vector3 towardHolder = Vector3.ProjectOnPlane(
                holderWorldPosition - gripCenter,
                bottleUp);
            if (towardHolder.sqrMagnitude < 0.000001f)
            {
                towardHolder = Vector3.ProjectOnPlane(
                    transform.forward,
                    bottleUp);
            }

            Vector3 underside = Vector3.ProjectOnPlane(
                Vector3.down,
                bottleUp);
            if (underside.sqrMagnitude < 0.000001f)
            {
                underside = towardHolder;
            }

            towardHolder.Normalize();
            underside.Normalize();
            float horizontal = 1f - Mathf.Abs(
                Vector3.Dot(bottleUp.normalized, Vector3.up));
            float undersideWeight = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.35f, 0.75f, horizontal));
            Vector3 radial = Vector3.Lerp(
                towardHolder,
                underside,
                undersideWeight);
            if (radial.sqrMagnitude < 0.000001f)
            {
                radial = underside.sqrMagnitude > 0.000001f
                    ? underside
                    : Vector3.forward;
            }

            return gripCenter +
                   radial.normalized * BottleHandSurfaceOffset;
        }

        public bool SetPourStreamFromCarriedBottle(
            Color color,
            float width = 0.018f)
        {
            if (!IsCarriedBottleVisible || activeVessel == null)
            {
                HidePourStream();
                return false;
            }

            return SetPourStream(
                CarriedBottleMouthWorldPosition,
                activeVessel.PourTargetWorldPosition,
                color,
                width);
        }

        public void HideCarriedBottle()
        {
            RestoreCarriedSource();
            DestroyCarriedBottleVisual();
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

        public bool SetActiveVesselAtBeerTap(float liftToSpout = 0f)
        {
            if (activeVessel == null || !HasBeerTapPresentation)
            {
                return false;
            }

            Pose pose = BeerTapVesselWorldPose;
            activeVessel.SetWorldPose(pose.position, pose.rotation);
            float lift = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(liftToSpout));
            Vector3 desiredPourTarget = Vector3.Lerp(
                activeVessel.PourTargetWorldPosition,
                BeerTapSpoutWorldPosition - transform.up * BeerTapPourGap,
                lift);
            activeVessel.transform.position +=
                desiredPourTarget - activeVessel.PourTargetWorldPosition;
            return true;
        }

        public void SetBeerTapBartenderContact(
            bool carryVessel,
            float vesselHandWeight,
            float handleHandWeight)
        {
            IsBeerTapVesselCarriedByBartender =
                carryVessel && activeVessel != null;
            BeerTapVesselHandWeight = Mathf.Clamp01(vesselHandWeight);
            BeerTapHandleHandWeight = Mathf.Clamp01(handleHandWeight);
        }

        public bool AlignActiveVesselGripTo(Transform carrier)
        {
            return activeVessel != null &&
                   activeVessel.AlignGripTo(carrier);
        }

        public bool AlignActiveVesselGripPositionTo(
            Transform carrier,
            Quaternion vesselRotation)
        {
            return activeVessel != null &&
                   activeVessel.AlignGripPositionTo(
                       carrier,
                       vesselRotation);
        }

        public float ResolveActiveVesselGripError(Transform carrier)
        {
            return activeVessel != null
                ? activeVessel.ResolveGripError(carrier)
                : float.PositiveInfinity;
        }

        public void SetBeerTapHandlePull(float amount)
        {
            BeerTapHandlePullAmount = Mathf.Clamp01(amount);
            ApplyBeerTapHandlePose();
        }

        public bool SetPourStreamFromBeerTap(
            Color color,
            float width = 0.018f)
        {
            if (!HasBeerTapPresentation || activeVessel == null)
            {
                HidePourStream();
                return false;
            }

            return SetPourStream(
                BeerTapSpoutWorldPosition,
                activeVessel.PourTargetWorldPosition,
                color,
                width);
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

            HideCarriedBottle();
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
            SetBeerTapBartenderContact(false, 0f, 0f);
            SetBeerTapHandlePull(0f);
            menuPresentation?.ResetPresentation();
        }

        private void ConfigureBeerTapRuntime()
        {
            if (beerTapHandlePivot == null || beerTapHandleGrip == null)
            {
                return;
            }

            beerTapHandleGripInPivot =
                beerTapHandlePivot.InverseTransformPoint(
                    beerTapHandleGrip.position);
            beerTapHandleGripRotationInPivot = Quaternion.Inverse(
                beerTapHandlePivot.rotation) *
                beerTapHandleGrip.rotation;
            if (beerTapHandleRoot != null)
            {
                beerTapHandleRestLocalPosition =
                    beerTapHandleRoot.localPosition;
                beerTapHandleRestLocalRotation =
                    beerTapHandleRoot.localRotation;
            }

            var gripObject = new GameObject(
                "Beer Tap Dynamic Handle Grip");
            beerTapDynamicHandleGrip = gripObject.transform;
            beerTapDynamicHandleGrip.SetParent(transform, false);
            ApplyBeerTapHandlePose();
        }

        private void ApplyBeerTapHandlePose()
        {
            if (beerTapHandlePivot == null ||
                beerTapDynamicHandleGrip == null)
            {
                return;
            }

            Quaternion pull = Quaternion.AngleAxis(
                BarBeerTapServicePlan.HandlePullDegrees *
                BeerTapHandlePullAmount,
                Vector3.right);
            beerTapDynamicHandleGrip.SetPositionAndRotation(
                beerTapHandlePivot.TransformPoint(
                    pull * beerTapHandleGripInPivot),
                beerTapHandlePivot.rotation * pull *
                beerTapHandleGripRotationInPivot);

            if (beerTapHandleRoot == null ||
                beerTapHandleRoot.parent == null)
            {
                return;
            }

            Transform parent = beerTapHandleRoot.parent;
            Vector3 pivotLocal = parent.InverseTransformPoint(
                beerTapHandlePivot.position);
            Vector3 axisLocal = parent.InverseTransformDirection(
                beerTapHandlePivot.right).normalized;
            Quaternion rootPull = Quaternion.AngleAxis(
                BarBeerTapServicePlan.HandlePullDegrees *
                BeerTapHandlePullAmount,
                axisLocal);
            beerTapHandleRoot.localPosition = pivotLocal + rootPull *
                (beerTapHandleRestLocalPosition - pivotLocal);
            beerTapHandleRoot.localRotation = rootPull *
                beerTapHandleRestLocalRotation;
        }

        private Pose ResolveWorldPose(
            Transform authored,
            BarDrinkServicePose fallback)
        {
            return authored != null
                ? new Pose(authored.position, authored.rotation)
                : new Pose(
                    transform.TransformPoint(fallback.Position),
                    transform.rotation * fallback.Rotation);
        }

        private void CaptureAndHideCarriedSource()
        {
            IReadOnlyList<Renderer> sourceRenderers =
                selectedBottle.Renderers;
            carriedSourceRenderers = new Renderer[sourceRenderers.Count];
            carriedSourceRendererStates = new bool[sourceRenderers.Count];
            for (int index = 0; index < sourceRenderers.Count; index++)
            {
                Renderer sourceRenderer = sourceRenderers[index];
                carriedSourceRenderers[index] = sourceRenderer;
                carriedSourceRendererStates[index] =
                    sourceRenderer != null && sourceRenderer.enabled;
                if (sourceRenderer != null)
                {
                    sourceRenderer.enabled = false;
                }
            }
        }

        private void RestoreCarriedSource()
        {
            for (int index = 0;
                 index < carriedSourceRenderers.Length;
                 index++)
            {
                Renderer sourceRenderer = carriedSourceRenderers[index];
                if (sourceRenderer != null)
                {
                    sourceRenderer.enabled =
                        carriedSourceRendererStates[index];
                }
            }

            carriedSourceRenderers = Array.Empty<Renderer>();
            carriedSourceRendererStates = Array.Empty<bool>();
        }

        private void DestroyCarriedBottleVisual()
        {
            if (carriedBottleRoot == null)
            {
                return;
            }

            GameObject carriedObject = carriedBottleRoot.gameObject;
            carriedObject.SetActive(false);
            carriedBottleRoot = null;
            carriedBottleContact = null;
            carriedBottleHeight = 0f;
            if (Application.isPlaying)
            {
                Destroy(carriedObject);
            }
            else
            {
                DestroyImmediate(carriedObject);
            }
        }

        private void OnDisable()
        {
            ResetPresentation();
        }

        private void OnDestroy()
        {
            RestoreCarriedSource();
            DestroyCarriedBottleVisual();
        }
    }
}
