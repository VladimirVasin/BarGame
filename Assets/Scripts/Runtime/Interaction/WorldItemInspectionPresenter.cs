using System;
using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Holds one real object up to the eye: it borrows the model out of the
    /// world, flies it to a camera-local pivot, scales it to fill a readable
    /// share of the frame, and hides everything behind it under a near quad.
    ///
    /// The refrigerator's shelf examination and the screen a thing picked off
    /// the ground opens are the same presentation, so the pivot, the backdrop
    /// and the fit-to-frame maths live here once rather than twice. The
    /// timeline that drives it is <see cref="WorldItemInspectionTimeline"/>.
    /// </summary>
    public sealed class WorldItemInspectionPresenter
    {
        public const float PreviewDistance = 0.68f;
        public const float BackdropDistance = 0.82f;
        public const float PreviewHeightFraction = 0.46f;
        public const float PreviewWidthFraction = 0.42f;
        public const float MinimumPreviewScale = 0.55f;
        public const float MaximumPreviewScale = 5.5f;

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");

        private readonly MaterialPropertyBlock properties =
            new MaterialPropertyBlock();

        private readonly Camera targetCamera;
        private readonly string label;

        private Transform pivot;
        private GameObject backdropObject;
        private Renderer backdropRenderer;

        private Transform heldRoot;
        private Transform savedParent;
        private int savedSiblingIndex;
        private Vector3 savedLocalPosition;
        private Quaternion savedLocalRotation;
        private Vector3 savedLocalScale;

        private Vector3 pivotStartLocalPosition;
        private Quaternion pivotStartLocalRotation;
        private Vector3 pivotTargetLocalPosition;
        private Quaternion pivotTargetLocalRotation;
        private float pivotTargetScale;

        public WorldItemInspectionPresenter(Camera camera, string label)
        {
            targetCamera = camera != null
                ? camera
                : throw new ArgumentNullException(nameof(camera));
            this.label = string.IsNullOrEmpty(label)
                ? "World Item Inspection"
                : label;
            EnsurePresentationObjects();
        }

        public Transform Pivot => pivot;
        public Renderer BackdropRenderer => backdropRenderer;
        public bool IsHolding => heldRoot != null;

        /// <summary>
        /// Takes the model out of the world and onto the camera-local pivot.
        /// The world pose is remembered so <see cref="Restore"/> can put it
        /// back exactly; a caller that consumes the object calls
        /// <see cref="Detach"/> instead.
        /// </summary>
        public bool Begin(
            Transform itemRoot,
            Bounds worldBounds,
            Quaternion baseRotation,
            float authoredScale)
        {
            if (itemRoot == null)
            {
                throw new ArgumentNullException(nameof(itemRoot));
            }

            if (IsHolding || pivot == null)
            {
                return false;
            }

            heldRoot = itemRoot;
            savedParent = itemRoot.parent;
            savedSiblingIndex = itemRoot.GetSiblingIndex();
            savedLocalPosition = itemRoot.localPosition;
            savedLocalRotation = itemRoot.localRotation;
            savedLocalScale = itemRoot.localScale;

            PreparePivot(worldBounds, baseRotation, authoredScale);
            itemRoot.SetParent(pivot, true);
            return true;
        }

        /// <summary>
        /// Applies one evaluated timeline frame and keeps the cinematic focus
        /// on the object while it is the thing being looked at.
        /// </summary>
        public void Apply(WorldItemInspectionFrame frame)
        {
            if (targetCamera == null)
            {
                return;
            }

            if (!IsHolding || pivot == null)
            {
                // Nothing is being held, but the veil still belongs to the
                // frame: an item taken out of the world leaves the screen
                // empty and the darkness has to lift rather than blink.
                ApplyBackdrop(frame.BackdropAlpha);
                return;
            }

            float blend = Mathf.Clamp01(frame.ItemBlend);
            pivot.localPosition = Vector3.Lerp(
                pivotStartLocalPosition,
                pivotTargetLocalPosition,
                blend);
            Quaternion rotatingTarget =
                pivotTargetLocalRotation *
                Quaternion.Euler(0f, frame.RotationDegrees, 0f);
            pivot.localRotation = Quaternion.Slerp(
                pivotStartLocalRotation,
                rotatingTarget,
                blend);
            pivot.localScale =
                Vector3.one * Mathf.Lerp(1f, pivotTargetScale, blend);
            ApplyBackdrop(frame.BackdropAlpha);
            CinematicDepthOfField.SetFocusDistance(
                Vector3.Distance(
                    targetCamera.transform.position,
                    pivot.position));
        }

        /// <summary>Puts the model back where it was lying.</summary>
        public bool Restore()
        {
            if (!IsHolding)
            {
                HideBackdrop();
                return false;
            }

            if (savedParent != null)
            {
                heldRoot.SetParent(savedParent, false);
                heldRoot.SetSiblingIndex(
                    Mathf.Clamp(
                        savedSiblingIndex,
                        0,
                        Mathf.Max(0, savedParent.childCount - 1)));
                heldRoot.localPosition = savedLocalPosition;
                heldRoot.localRotation = savedLocalRotation;
                heldRoot.localScale = savedLocalScale;
            }
            else if (heldRoot != null)
            {
                // Whatever owned this model is gone while we were holding it.
                // It cannot go home, and left on the pivot it would hang in
                // front of the eye for as long as the camera lives.
                DestroyOwnedObject(heldRoot.gameObject);
            }

            ClearHeld();
            return true;
        }

        /// <summary>
        /// Releases the model without restoring it, for a caller that is
        /// about to destroy the object the player has just taken.
        /// </summary>
        public bool Detach()
        {
            if (!IsHolding)
            {
                HideBackdrop();
                return false;
            }

            ClearHeld();
            return true;
        }

        public void Dispose()
        {
            Restore();
            DestroyOwnedObject(backdropObject);
            if (pivot != null)
            {
                DestroyOwnedObject(pivot.gameObject);
            }

            backdropObject = null;
            backdropRenderer = null;
            pivot = null;
        }

        public static Bounds CalculateWorldBounds(Transform root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(root.position, Vector3.one * 0.1f);
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private void ClearHeld()
        {
            heldRoot = null;
            savedParent = null;
            HideBackdrop();
        }

        private void PreparePivot(
            Bounds itemBounds,
            Quaternion baseRotation,
            float authoredScale)
        {
            pivot.SetParent(targetCamera.transform, true);
            pivot.position = itemBounds.center;
            pivot.rotation = Quaternion.identity;
            pivot.localScale = Vector3.one;
            pivotStartLocalPosition = pivot.localPosition;
            pivotStartLocalRotation = pivot.localRotation;
            pivotTargetLocalPosition =
                new Vector3(0f, 0.035f, PreviewDistance);
            pivotTargetLocalRotation = baseRotation;
            pivotTargetScale =
                CalculatePreviewScale(itemBounds, authoredScale);
        }

        private float CalculatePreviewScale(
            Bounds bounds,
            float authoredScale)
        {
            float viewHeight;
            if (targetCamera.orthographic)
            {
                viewHeight = targetCamera.orthographicSize * 2f;
            }
            else
            {
                viewHeight =
                    2f *
                    PreviewDistance *
                    Mathf.Tan(
                        targetCamera.fieldOfView *
                        Mathf.Deg2Rad *
                        0.5f);
            }

            float viewWidth = viewHeight * targetCamera.aspect;
            float heightScale =
                viewHeight * PreviewHeightFraction /
                Mathf.Max(0.01f, bounds.size.y);
            float widthScale =
                viewWidth * PreviewWidthFraction /
                Mathf.Max(0.01f, bounds.size.x);
            return Mathf.Clamp(
                Mathf.Min(heightScale, widthScale) * authoredScale,
                MinimumPreviewScale,
                MaximumPreviewScale);
        }

        private void ApplyBackdrop(float itemBlend)
        {
            if (backdropRenderer == null)
            {
                return;
            }

            float reveal = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.62f, 1f, itemBlend));
            if (reveal <= 0.001f)
            {
                HideBackdrop();
                return;
            }

            UpdateBackdropGeometry();
            backdropObject.SetActive(true);
            properties.Clear();
            backdropRenderer.GetPropertyBlock(properties);
            properties.SetColor(
                BaseColorId,
                new Color(0.005f, 0.004f, 0.008f, 0.86f * reveal));
            backdropRenderer.SetPropertyBlock(properties);
        }

        private void UpdateBackdropGeometry()
        {
            float height;
            if (targetCamera.orthographic)
            {
                height = targetCamera.orthographicSize * 2f;
            }
            else
            {
                height =
                    2f *
                    BackdropDistance *
                    Mathf.Tan(
                        targetCamera.fieldOfView *
                        Mathf.Deg2Rad *
                        0.5f);
            }

            float width = height * targetCamera.aspect;
            backdropObject.transform.localPosition =
                new Vector3(0f, 0f, BackdropDistance);
            backdropObject.transform.localRotation = Quaternion.identity;
            backdropObject.transform.localScale =
                new Vector3(width * 1.08f, height * 1.08f, 1f);
        }

        private void EnsurePresentationObjects()
        {
            var pivotObject = new GameObject($"{label} Pivot")
            {
                hideFlags = HideFlags.DontSave
            };
            pivot = pivotObject.transform;
            pivot.SetParent(targetCamera.transform, false);

            backdropObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backdropObject.name = $"{label} Backdrop";
            backdropObject.hideFlags = HideFlags.DontSave;
            backdropObject.transform.SetParent(
                targetCamera.transform,
                false);
            Collider backdropCollider =
                backdropObject.GetComponent<Collider>();
            if (backdropCollider != null)
            {
                backdropCollider.enabled = false;
                DestroyOwnedObject(backdropCollider);
            }

            backdropRenderer = backdropObject.GetComponent<Renderer>();
            backdropRenderer.sharedMaterial =
                HomeBalconyResources.GlassMaterial;
            backdropRenderer.shadowCastingMode = ShadowCastingMode.Off;
            backdropRenderer.receiveShadows = false;
            backdropRenderer.lightProbeUsage = LightProbeUsage.Off;
            backdropRenderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            HideBackdrop();
        }

        private void HideBackdrop()
        {
            if (backdropObject != null)
            {
                backdropObject.SetActive(false);
            }
        }

        private static void DestroyOwnedObject(UnityEngine.Object value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(value);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }
    }
}
