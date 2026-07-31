using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Owns one real bottle model and the complete snapshot required to put it
    /// back after selection, pouring, cancellation or scene teardown.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BarDrinkBottleView : MonoBehaviour
    {
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private DrinkId drinkId;
        [SerializeField] private string slotId;
        [SerializeField] private Transform mouthAnchor;
        [SerializeField] private Renderer[] bottleRenderers =
            Array.Empty<Renderer>();
        [SerializeField] private Collider solidCollider;
        [SerializeField] private Collider selectionTrigger;
        [SerializeField] private Rigidbody bottleBody;

        private ReadOnlyCollection<Renderer> renderersView;
        private Color[] baseColors = Array.Empty<Color>();
        private bool[] originalRendererEnabled = Array.Empty<bool>();
        private MaterialPropertyBlock colorProperties;
        private Transform originalParent;
        private int originalSiblingIndex;
        private Vector3 originalLocalPosition;
        private Quaternion originalLocalRotation;
        private Vector3 originalLocalScale;
        private bool originalSolidEnabled;
        private bool originalSolidIsTrigger;
        private bool originalSelectionEnabled;
        private bool originalSelectionIsTrigger;
        private bool originalBodyKinematic;
        private bool originalBodyUseGravity;
        private bool originalBodyDetectCollisions;
        private RigidbodyConstraints originalBodyConstraints;
        private RigidbodyInterpolation originalBodyInterpolation;
        private CollisionDetectionMode originalCollisionMode;
        private bool initialized;

        public DrinkId DrinkId => drinkId;
        public string SlotId => slotId;
        public Transform Root => transform;
        public Transform OriginalRoot => transform;
        public Transform OriginalParent => originalParent;
        public Vector3 OriginalLocalPosition => originalLocalPosition;
        public Quaternion OriginalLocalRotation => originalLocalRotation;
        public Vector3 OriginalLocalScale => originalLocalScale;
        public Transform MouthAnchor => mouthAnchor;
        public Vector3 MouthWorldPosition =>
            mouthAnchor != null ? mouthAnchor.position : transform.position;
        public Quaternion MouthWorldRotation =>
            mouthAnchor != null ? mouthAnchor.rotation : transform.rotation;
        public IReadOnlyList<Renderer> Renderers =>
            renderersView ??= Array.AsReadOnly(
                bottleRenderers ?? Array.Empty<Renderer>());
        public Collider SolidCollider => solidCollider;
        public Collider SelectionTrigger => selectionTrigger;
        public Rigidbody Body => bottleBody;
        public bool IsInitialized => initialized;

        internal void Initialize(
            DrinkId newDrinkId,
            string newSlotId,
            Transform newMouthAnchor,
            IReadOnlyList<Renderer> newRenderers,
            IReadOnlyList<Color> newBaseColors,
            Collider newSolidCollider,
            Collider newSelectionTrigger,
            Rigidbody newBody)
        {
            if (newDrinkId == DrinkId.None ||
                newDrinkId == DrinkId.Moonshine ||
                !BarDrinkCatalog.TryGetOffer(newDrinkId, out _))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(newDrinkId),
                    "A bottle view requires a retail drink ID.");
            }

            if (string.IsNullOrWhiteSpace(newSlotId))
            {
                throw new ArgumentException(
                    "A bottle view requires a stable slot ID.",
                    nameof(newSlotId));
            }

            if (newMouthAnchor == null ||
                !newMouthAnchor.IsChildOf(transform))
            {
                throw new ArgumentException(
                    "The mouth anchor must belong to the bottle root.",
                    nameof(newMouthAnchor));
            }

            if (newRenderers == null ||
                newBaseColors == null ||
                newRenderers.Count == 0 ||
                newRenderers.Count != newBaseColors.Count)
            {
                throw new ArgumentException(
                    "Bottle renderers and base colors must be non-empty and aligned.",
                    nameof(newRenderers));
            }

            var rendererCopy = new Renderer[newRenderers.Count];
            var colorCopy = new Color[newBaseColors.Count];
            var enabledCopy = new bool[newRenderers.Count];
            for (int index = 0; index < newRenderers.Count; index++)
            {
                Renderer bottleRenderer = newRenderers[index];
                if (bottleRenderer == null ||
                    !bottleRenderer.transform.IsChildOf(transform))
                {
                    throw new ArgumentException(
                        "Every renderer must belong to its bottle root.",
                        nameof(newRenderers));
                }

                rendererCopy[index] = bottleRenderer;
                colorCopy[index] = newBaseColors[index];
                enabledCopy[index] = bottleRenderer.enabled;
            }

            if (newSolidCollider == null ||
                newSolidCollider.transform != transform ||
                newSolidCollider.isTrigger)
            {
                throw new ArgumentException(
                    "The solid bottle collider must be non-trigger geometry on " +
                    "the bottle root.",
                    nameof(newSolidCollider));
            }

            if (newSelectionTrigger == null ||
                newSelectionTrigger.transform != transform ||
                !newSelectionTrigger.isTrigger)
            {
                throw new ArgumentException(
                    "The selection collider must be a trigger on the bottle root.",
                    nameof(newSelectionTrigger));
            }

            if (newBody == null || newBody.transform != transform)
            {
                throw new ArgumentException(
                    "The bottle Rigidbody must live on the bottle root.",
                    nameof(newBody));
            }

            drinkId = newDrinkId;
            slotId = newSlotId.Trim();
            mouthAnchor = newMouthAnchor;
            bottleRenderers = rendererCopy;
            baseColors = colorCopy;
            originalRendererEnabled = enabledCopy;
            solidCollider = newSolidCollider;
            selectionTrigger = newSelectionTrigger;
            bottleBody = newBody;
            renderersView = Array.AsReadOnly(rendererCopy);
            colorProperties = new MaterialPropertyBlock();

            originalParent = transform.parent;
            originalSiblingIndex = transform.GetSiblingIndex();
            originalLocalPosition = transform.localPosition;
            originalLocalRotation = transform.localRotation;
            originalLocalScale = transform.localScale;
            originalSolidEnabled = solidCollider.enabled;
            originalSolidIsTrigger = solidCollider.isTrigger;
            originalSelectionEnabled = selectionTrigger.enabled;
            originalSelectionIsTrigger = selectionTrigger.isTrigger;
            originalBodyKinematic = bottleBody.isKinematic;
            originalBodyUseGravity = bottleBody.useGravity;
            originalBodyDetectCollisions = bottleBody.detectCollisions;
            originalBodyConstraints = bottleBody.constraints;
            originalBodyInterpolation = bottleBody.interpolation;
            originalCollisionMode = bottleBody.collisionDetectionMode;
            initialized = true;
            SetSelectionHighlight(0f);
        }

        public void SetSelectionHighlight(float amount)
        {
            if (!initialized)
            {
                return;
            }

            float highlight = Mathf.Clamp01(amount);
            for (int index = 0; index < bottleRenderers.Length; index++)
            {
                Renderer bottleRenderer = bottleRenderers[index];
                if (bottleRenderer == null)
                {
                    continue;
                }

                Color baseColor = baseColors[index];
                Color selectedColor = new Color(
                    baseColor.r * 1.28f + 0.20f,
                    baseColor.g * 1.18f + 0.11f,
                    baseColor.b * 0.88f + 0.025f,
                    baseColor.a);
                Color displayed = Color.Lerp(
                    baseColor,
                    selectedColor,
                    highlight);
                bottleRenderer.GetPropertyBlock(colorProperties);
                colorProperties.SetColor(BaseColorId, displayed);
                colorProperties.SetColor(ColorId, displayed);
                bottleRenderer.SetPropertyBlock(colorProperties);
            }
        }

        public void SetWorldPose(Vector3 position, Quaternion rotation)
        {
            if (!initialized)
            {
                return;
            }

            transform.SetPositionAndRotation(position, rotation);
        }

        public void SetShelfPresentation(float amount)
        {
            if (!initialized)
            {
                return;
            }

            float presented = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(amount));
            transform.SetParent(originalParent, false);
            transform.localPosition = originalLocalPosition +
                new Vector3(0f, 0.045f, -0.12f) * presented;
            transform.localRotation = originalLocalRotation *
                Quaternion.Euler(0f, 7f * presented, 0f);
            transform.localScale = originalLocalScale;
            SetSelectionHighlight(presented);
        }

        public void SetLocalPose(
            BarDrinkServicePose pose,
            Transform referenceSpace)
        {
            if (!initialized)
            {
                return;
            }

            if (referenceSpace == null)
            {
                SetWorldPose(pose.Position, pose.Rotation);
                return;
            }

            SetWorldPose(
                referenceSpace.TransformPoint(pose.Position),
                referenceSpace.rotation * pose.Rotation);
        }

        public void SetColliderState(
            bool solidEnabled,
            bool selectionEnabled)
        {
            if (!initialized)
            {
                return;
            }

            solidCollider.enabled = solidEnabled;
            selectionTrigger.enabled = selectionEnabled;
        }

        public void ResetExact()
        {
            if (!initialized)
            {
                return;
            }

            transform.SetParent(originalParent, false);
            // Unity rejects sibling reordering from OnDisable while an
            // ancestor is itself deactivating (for example during a Single
            // scene unload). Normal presentation cleanup runs while the bar
            // is active and still restores the exact authored order.
            if (originalParent != null &&
                gameObject.activeInHierarchy &&
                originalParent.gameObject.activeInHierarchy)
            {
                int maximumIndex = Mathf.Max(
                    0,
                    originalParent.childCount - 1);
                transform.SetSiblingIndex(Mathf.Min(
                    originalSiblingIndex,
                    maximumIndex));
            }

            transform.localPosition = originalLocalPosition;
            transform.localRotation = originalLocalRotation;
            transform.localScale = originalLocalScale;
            solidCollider.enabled = originalSolidEnabled;
            solidCollider.isTrigger = originalSolidIsTrigger;
            selectionTrigger.enabled = originalSelectionEnabled;
            selectionTrigger.isTrigger = originalSelectionIsTrigger;
            bottleBody.isKinematic = originalBodyKinematic;
            bottleBody.useGravity = originalBodyUseGravity;
            bottleBody.detectCollisions = originalBodyDetectCollisions;
            bottleBody.constraints = originalBodyConstraints;
            bottleBody.interpolation = originalBodyInterpolation;
            bottleBody.collisionDetectionMode = originalCollisionMode;
            for (int index = 0; index < bottleRenderers.Length; index++)
            {
                if (bottleRenderers[index] != null)
                {
                    bottleRenderers[index].enabled =
                        originalRendererEnabled[index];
                }
            }

            SetSelectionHighlight(0f);
        }

        private void OnDisable()
        {
            ResetExact();
        }
    }
}
