using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
        public const float PreviewHeightFraction = 0.40f;
        public const float PreviewWidthFraction = 0.42f;
        public const float MinimumPreviewScale = 0.55f;
        public const float MaximumPreviewScale = 5.5f;
        public const float InspectionAperture = 8f;
        public const float InspectionFocalLength = 35f;
        // Bit 4 belongs to the seated toilet episode. This pair lights only
        // the borrowed inspection model, without lifting the surrounding world.
        private const uint InspectionLightLayer = 1u << 5;

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");

        private readonly MaterialPropertyBlock properties =
            new MaterialPropertyBlock();

        private readonly Camera targetCamera;
        private readonly string label;

        private Transform pivot;
        private GameObject backdropObject;
        private Renderer backdropRenderer;
        private GameObject lightingRoot;
        private readonly List<Renderer> litRenderers = new List<Renderer>(8);
        private readonly List<uint> savedRenderingLayers = new List<uint>(8);

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
        private UniversalAdditionalCameraData inspectionCameraData;
        private bool inspectionCameraExcluded;
        private bool depthOfFieldAttempted;

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
        public bool OwnsDepthOfField =>
            CinematicDepthOfField.IsOwnedBy(this) && CinematicDepthOfField.IsActive;
        public float FocusDistance => targetCamera != null && pivot != null
            ? Mathf.Max(CinematicDepthOfField.MinimumFocusDistance,
                Vector3.Dot(pivot.position - targetCamera.transform.position, targetCamera.transform.forward))
            : 0f;

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
            depthOfFieldAttempted = false;
            targetCamera.TryGetComponent(out inspectionCameraData);
            inspectionCameraExcluded = targetCamera.TryGetComponent<ScenePresentationExclusion>(out _);

            PreparePivot(worldBounds, baseRotation, authoredScale);
            itemRoot.SetParent(pivot, true);
            BeginLighting(itemRoot);
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
                EndDepthOfField();
                return;
            }

            if (!IsHolding || pivot == null)
            {
                // Nothing is being held, but the veil still belongs to the
                // frame: an item taken out of the world leaves the screen
                // empty and the darkness has to lift rather than blink.
                EndDepthOfField();
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
            UpdateLightingPosition();
            ApplyBackdrop(frame.BackdropAlpha);
            ApplyDepthOfField(frame.Phase);
        }

        /// <summary>Puts the model back where it was lying.</summary>
        public bool Restore()
        {
            EndDepthOfField();
            if (!IsHolding)
            {
                EndLighting();
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
            EndDepthOfField();
            if (!IsHolding)
            {
                EndLighting();
                HideBackdrop();
                return false;
            }

            ClearHeld();
            return true;
        }

        public void Dispose()
        {
            Restore();
            DestroyOwnedObject(lightingRoot);
            DestroyOwnedObject(backdropObject);
            if (pivot != null)
            {
                DestroyOwnedObject(pivot.gameObject);
            }

            backdropObject = null;
            backdropRenderer = null;
            lightingRoot = null;
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
            EndDepthOfField();
            EndLighting();
            heldRoot = null;
            savedParent = null;
            HideBackdrop();
        }

        private void ApplyDepthOfField(WorldItemInspectionPhase phase)
        {
            // Shelf/service shots retain their outer owner's optics. A held
            // collectible may borrow the shared lens only once it is settled.
            bool eligible = phase == WorldItemInspectionPhase.Inspecting &&
                heldRoot.gameObject.activeInHierarchy &&
                targetCamera.isActiveAndEnabled && !targetCamera.orthographic &&
                targetCamera.cameraType == CameraType.Game &&
                inspectionCameraData != null && inspectionCameraData.renderPostProcessing &&
                (inspectionCameraData.volumeLayerMask.value & 1) != 0 &&
                !inspectionCameraExcluded &&
                GraphicsEffectsSettings.DepthOfFieldEnabled;
            if (!eligible)
            {
                EndDepthOfField();
                return;
            }
            if (PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused) return;
            if (!depthOfFieldAttempted)
            {
                depthOfFieldAttempted = true;
                CinematicDepthOfField.TryBeginOwned(this, FocusDistance,
                    InspectionAperture, InspectionFocalLength);
            }
            CinematicDepthOfField.SetOwnedFocusDistance(this, FocusDistance);
        }

        private void EndDepthOfField() => CinematicDepthOfField.EndOwned(this, true);

        private void BeginLighting(Transform itemRoot)
        {
            if (lightingRoot == null)
            {
                lightingRoot = new GameObject($"{label} Lighting") { hideFlags = HideFlags.DontSave };
                lightingRoot.transform.SetParent(targetCamera.transform, false);
                BuildInspectionLight("Key", new Vector3(-.55f, .65f, -.05f),
                    new Color(1f, .96f, .9f), 1.3f);
                BuildInspectionLight("Fill", new Vector3(.55f, -.15f, .1f),
                    new Color(.9f, .95f, 1f), .7f);
            }
            litRenderers.Clear();
            savedRenderingLayers.Clear();
            itemRoot.GetComponentsInChildren(true, litRenderers);
            foreach (Renderer renderer in litRenderers)
            {
                savedRenderingLayers.Add(renderer.renderingLayerMask);
                renderer.renderingLayerMask = InspectionLightLayer;
            }
            UpdateLightingPosition();
            lightingRoot.SetActive(true);
        }

        private void UpdateLightingPosition()
        {
            // Follow translation through the lift/return, but never inherit
            // the item's rotation or fit-to-frame scale: light stays readable.
            if (lightingRoot != null && pivot != null)
                lightingRoot.transform.localPosition = pivot.localPosition -
                    new Vector3(0f, .035f, PreviewDistance);
        }

        private void BuildInspectionLight(string role, Vector3 position, Color color, float intensity)
        {
            var carrier = new GameObject($"{label} {role} Light") { hideFlags = HideFlags.DontSave };
            carrier.transform.SetParent(lightingRoot.transform, false);
            carrier.transform.localPosition = position;
            Light light = carrier.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = 2.5f;
            light.shadows = LightShadows.None;
            light.bounceIntensity = 0f;
            light.GetUniversalAdditionalLightData().renderingLayers = InspectionLightLayer;
        }

        private void EndLighting()
        {
            if (lightingRoot != null) lightingRoot.SetActive(false);
            for (int index = 0; index < litRenderers.Count; index++)
                if (litRenderers[index] != null)
                    litRenderers[index].renderingLayerMask = savedRenderingLayers[index];
            litRenderers.Clear();
            savedRenderingLayers.Clear();
        }

        private void PreparePivot(
            Bounds itemBounds,
            Quaternion baseRotation,
            float authoredScale)
        {
            pivot.SetParent(targetCamera.transform, true);
            pivot.position = itemBounds.center;
            // Borrow the item's authored frame as well as its centre. A cabinet
            // turned in the world must still present the label in the same held pose.
            pivot.rotation = heldRoot.rotation;
            pivot.localScale = Vector3.one;
            pivotStartLocalPosition = pivot.localPosition;
            pivotStartLocalRotation = pivot.localRotation;
            pivotTargetLocalPosition =
                new Vector3(0f, 0.060f, PreviewDistance);
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
