using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Runtime references and presentation controls for the generated home
    /// refrigerator. Geometry stays data-first in the world builder while the
    /// interaction owns timing and simply applies normalized channels here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeRefrigeratorView : MonoBehaviour
    {
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");

        [SerializeField] private Transform doorPivot;
        [SerializeField] private Transform doorLeaf;
        [SerializeField] private Transform handlePivot;
        [SerializeField] private Transform interiorRoot;
        [SerializeField] private Renderer interiorLightStrip;
        [SerializeField] private CityLightHalo interiorHalo;
        [SerializeField] private Collider bodyCollider;

        private IReadOnlyDictionary<string, Transform> slotRoots =
            new ReadOnlyDictionary<string, Transform>(
                new Dictionary<string, Transform>());
        private Quaternion doorClosedRotation;
        private Quaternion handleClosedRotation;
        private Vector3 haloFullScale = Vector3.one;
        private Color interiorLightColor = Color.white;
        private float doorOpenAngle;
        private MaterialPropertyBlock lightProperties;
        private bool initialized;

        public Transform DoorPivot => doorPivot;
        public Transform DoorLeaf => doorLeaf;
        public Transform HandlePivot => handlePivot;
        public Transform InteriorRoot => interiorRoot;
        public Renderer InteriorLightStrip => interiorLightStrip;
        public CityLightHalo InteriorHalo => interiorHalo;
        public Collider BodyCollider => bodyCollider;
        public IReadOnlyDictionary<string, Transform> SlotRoots => slotRoots;
        public float DoorOpenAmount { get; private set; }
        public float HandleTurnAmount { get; private set; }
        public float InteriorLightAmount { get; private set; }

        internal void Initialize(
            Transform newDoorPivot,
            Transform newDoorLeaf,
            Transform newHandlePivot,
            Transform newInteriorRoot,
            Renderer newInteriorLightStrip,
            CityLightHalo newInteriorHalo,
            Collider newBodyCollider,
            float newDoorOpenAngle,
            Color newInteriorLightColor,
            IDictionary<string, Transform> newSlotRoots)
        {
            doorPivot = newDoorPivot != null
                ? newDoorPivot
                : throw new ArgumentNullException(nameof(newDoorPivot));
            doorLeaf = newDoorLeaf != null
                ? newDoorLeaf
                : throw new ArgumentNullException(nameof(newDoorLeaf));
            handlePivot = newHandlePivot != null
                ? newHandlePivot
                : throw new ArgumentNullException(nameof(newHandlePivot));
            interiorRoot = newInteriorRoot != null
                ? newInteriorRoot
                : throw new ArgumentNullException(nameof(newInteriorRoot));
            interiorLightStrip = newInteriorLightStrip != null
                ? newInteriorLightStrip
                : throw new ArgumentNullException(
                    nameof(newInteriorLightStrip));
            interiorHalo = newInteriorHalo != null
                ? newInteriorHalo
                : throw new ArgumentNullException(nameof(newInteriorHalo));
            bodyCollider = newBodyCollider != null
                ? newBodyCollider
                : throw new ArgumentNullException(nameof(newBodyCollider));
            if (float.IsNaN(newDoorOpenAngle) ||
                float.IsInfinity(newDoorOpenAngle) ||
                Mathf.Abs(newDoorOpenAngle) < 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(newDoorOpenAngle),
                    "The refrigerator door requires a finite opening angle.");
            }

            if (newSlotRoots == null)
            {
                throw new ArgumentNullException(nameof(newSlotRoots));
            }

            doorOpenAngle = newDoorOpenAngle;
            interiorLightColor = newInteriorLightColor;
            doorClosedRotation = doorPivot.localRotation;
            handleClosedRotation = handlePivot.localRotation;
            haloFullScale = interiorHalo.transform.localScale;
            slotRoots = new ReadOnlyDictionary<string, Transform>(
                new Dictionary<string, Transform>(newSlotRoots));
            lightProperties = new MaterialPropertyBlock();
            initialized = true;
            ResetPresentation();
        }

        /// <summary>
        /// Applies all refrigerator animation channels in one call. Door values
        /// slightly above one are accepted so the timeline's restrained
        /// overshoot is preserved.
        /// </summary>
        public void ApplyPresentation(
            float doorAmount,
            float handleAmount,
            float lightAmount)
        {
            SetDoorOpenAmount(doorAmount);
            SetHandleTurnAmount(handleAmount);
            SetInteriorLightAmount(lightAmount);
        }

        public void SetDoorOpenAmount(float amount)
        {
            if (!initialized)
            {
                return;
            }

            DoorOpenAmount = Mathf.Clamp(amount, -0.05f, 1.10f);
            doorPivot.localRotation =
                doorClosedRotation *
                Quaternion.Euler(
                    0f,
                    doorOpenAngle * DoorOpenAmount,
                    0f);
        }

        public void SetHandleTurnAmount(float amount)
        {
            if (!initialized)
            {
                return;
            }

            HandleTurnAmount = Mathf.Clamp01(amount);
            handlePivot.localRotation =
                handleClosedRotation *
                Quaternion.Euler(
                    -11f * HandleTurnAmount,
                    0f,
                    -3f * HandleTurnAmount);
        }

        public void SetInteriorLightAmount(float amount)
        {
            if (!initialized)
            {
                return;
            }

            InteriorLightAmount = Mathf.Clamp01(amount);
            bool visible = InteriorLightAmount > 0.001f;
            interiorLightStrip.enabled = visible;
            interiorHalo.SetVisible(visible);
            interiorHalo.transform.localScale =
                haloFullScale * Mathf.Lerp(
                    0.12f,
                    1f,
                    Mathf.SmoothStep(0f, 1f, InteriorLightAmount));

            interiorLightStrip.GetPropertyBlock(lightProperties);
            Color displayedColor = Color.Lerp(
                interiorLightColor * 0.08f,
                interiorLightColor,
                InteriorLightAmount);
            displayedColor.a = 1f;
            lightProperties.SetColor(BaseColorId, displayedColor);
            lightProperties.SetColor(ColorId, displayedColor);
            interiorLightStrip.SetPropertyBlock(lightProperties);
        }

        public bool TryGetSlot(string id, out Transform slotRoot)
        {
            if (string.IsNullOrEmpty(id))
            {
                slotRoot = null;
                return false;
            }

            return slotRoots.TryGetValue(id, out slotRoot);
        }

        public void ResetPresentation()
        {
            if (!initialized)
            {
                return;
            }

            SetDoorOpenAmount(0f);
            SetHandleTurnAmount(0f);
            SetInteriorLightAmount(0f);
        }

        private void OnDisable()
        {
            ResetPresentation();
        }
    }

    /// <summary>
    /// Stable runtime identity for one physical refrigerator storage slot.
    /// Item systems can later bind inventory state without searching names or
    /// inferring dimensions from generated renderers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeRefrigeratorSlotView : MonoBehaviour
    {
        [SerializeField] private string slotId;
        [SerializeField] private HomeRefrigeratorSlotParent parentKind;
        [SerializeField] private Vector3 size;
        [SerializeField] private HomeRefrigeratorItemKind initialOccupant;

        public string SlotId => slotId;
        public HomeRefrigeratorSlotParent ParentKind => parentKind;
        public Vector3 Size => size;
        public HomeRefrigeratorItemKind InitialOccupant => initialOccupant;
        public bool IsInitiallyOccupied =>
            initialOccupant != HomeRefrigeratorItemKind.None;

        internal void Initialize(HomeRefrigeratorSlotPlan plan)
        {
            slotId = plan.Id;
            parentKind = plan.Parent;
            size = plan.Size;
            initialOccupant = plan.Occupant;
        }
    }
}
