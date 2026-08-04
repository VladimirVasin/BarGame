using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Camera-local right arm used while the refrigerator owns the Home
    /// camera. Its visible parts come from the production Player3D prefab and
    /// its grip keeps following the live door handle while that door swings.
    /// </summary>
    [DefaultExecutionOrder(290)]
    [DisallowMultipleComponent]
    public sealed class HomeRefrigeratorFirstPersonHand : MonoBehaviour
    {
        private const float VisibilityThreshold = 0.002f;
        private const float GripForwardOffset = 0.047f;
        private const float GripVerticalOffset = 0.105f;

        private static readonly Vector3 StartLocalPosition =
            new Vector3(0.34f, -0.34f, 0.46f);
        private static readonly Quaternion StartLocalRotation =
            Quaternion.Euler(8f, 164f, -16f);

        private Camera targetCamera;
        private Transform handleTarget;
        private Transform presentationRoot;
        private Transform handModelRoot;
        private Player3DFirstPersonSubset armSubset;

        public bool IsInitialized { get; private set; }
        public bool IsVisible =>
            presentationRoot != null &&
            presentationRoot.gameObject.activeSelf;
        public float ReachAmount { get; private set; }
        public Camera TargetCamera => targetCamera;
        public Transform HandleTarget => handleTarget;
        public Transform PresentationRoot => presentationRoot;
        public Transform HandModelRoot => handModelRoot;
        public Player3DAssetRegistry ModelRegistry => armSubset?.Registry;

        public void Initialize(
            Camera camera,
            Transform newHandleTarget)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            if (newHandleTarget == null)
            {
                throw new ArgumentNullException(nameof(newHandleTarget));
            }

            ReleasePresentation();
            targetCamera = camera;
            handleTarget = newHandleTarget;
            try
            {
                BuildPresentation();
                IsInitialized = true;
                ResetPresentation();
            }
            catch
            {
                ReleasePresentation();
                targetCamera = null;
                handleTarget = null;
                IsInitialized = false;
                throw;
            }
        }

        public void SetHandleTarget(Transform newHandleTarget)
        {
            if (newHandleTarget == null)
            {
                throw new ArgumentNullException(nameof(newHandleTarget));
            }

            handleTarget = newHandleTarget;
            if (IsVisible)
            {
                RefreshPose();
            }
        }

        public void ApplyReach(float normalizedReach)
        {
            if (float.IsNaN(normalizedReach) ||
                float.IsInfinity(normalizedReach))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(normalizedReach),
                    "Hand reach must be finite.");
            }

            ReachAmount = Mathf.Clamp01(normalizedReach);
            if (!IsInitialized || presentationRoot == null)
            {
                return;
            }

            bool shouldBeVisible =
                ReachAmount > VisibilityThreshold &&
                targetCamera != null &&
                handleTarget != null;
            presentationRoot.gameObject.SetActive(shouldBeVisible);
            if (shouldBeVisible)
            {
                RefreshPose();
            }
        }

        public void Hide()
        {
            ReachAmount = 0f;
            if (presentationRoot != null)
            {
                presentationRoot.gameObject.SetActive(false);
            }
        }

        public void ResetPresentation()
        {
            Hide();
        }

        private void LateUpdate()
        {
            if (!IsVisible)
            {
                return;
            }

            if (targetCamera == null || handleTarget == null)
            {
                Hide();
                return;
            }

            RefreshPose();
        }

        private void OnDisable()
        {
            Hide();
        }

        private void OnDestroy()
        {
            ReleasePresentation();
            targetCamera = null;
            handleTarget = null;
            IsInitialized = false;
        }

        private void BuildPresentation()
        {
            presentationRoot =
                new GameObject(
                    "Home Refrigerator First Person Hand").transform;
            presentationRoot.SetParent(targetCamera.transform, false);
            presentationRoot.gameObject.layer =
                targetCamera.gameObject.layer;
            presentationRoot.gameObject.SetActive(false);

            handModelRoot = new GameObject("Hand Model").transform;
            handModelRoot.SetParent(presentationRoot, false);
            handModelRoot.gameObject.layer = targetCamera.gameObject.layer;
            // Preserve the existing reach trajectory: its root ends below the
            // handle while the production grip socket lands on the handle.
            handModelRoot.localPosition =
                Vector3.up * GripVerticalOffset;
            armSubset = Player3DFirstPersonSubset.Create(
                handModelRoot,
                Player3DFirstPersonSide.Right,
                targetCamera.gameObject.layer,
                "Player3D Refrigerator Right Arm Subset");
        }

        private void RefreshPose()
        {
            float easedReach = SmootherStep(ReachAmount);
            Transform cameraTransform = targetCamera.transform;

            Vector3 targetUp = handleTarget.up;
            if (targetUp.sqrMagnitude < 0.0001f)
            {
                targetUp = Vector3.up;
            }
            else
            {
                targetUp.Normalize();
            }

            Vector3 towardCamera =
                cameraTransform.position - handleTarget.position;
            if (towardCamera.sqrMagnitude < 0.0001f)
            {
                towardCamera = -cameraTransform.forward;
            }
            else
            {
                towardCamera.Normalize();
            }

            Quaternion gripWorldRotation =
                Quaternion.LookRotation(towardCamera, targetUp);
            // LookRotation orthogonalizes its up vector when the handle-to-
            // camera direction has a vertical component. Offset along that
            // actual local up so the model's raised grip socket cancels it
            // exactly instead of missing the handle by a few millimetres.
            Vector3 modelUp = gripWorldRotation * Vector3.up;
            Vector3 gripWorldPosition =
                handleTarget.position +
                towardCamera * GripForwardOffset -
                modelUp * GripVerticalOffset;
            Vector3 gripLocalPosition =
                cameraTransform.InverseTransformPoint(gripWorldPosition);
            float safeDepth = targetCamera.nearClipPlane + 0.08f;
            gripLocalPosition.z =
                Mathf.Max(safeDepth, gripLocalPosition.z);

            Vector3 control =
                Vector3.Lerp(StartLocalPosition, gripLocalPosition, 0.53f) +
                new Vector3(0.075f, -0.055f, -0.045f);
            presentationRoot.localPosition = QuadraticBezier(
                StartLocalPosition,
                control,
                gripLocalPosition,
                easedReach);

            Quaternion gripLocalRotation =
                Quaternion.Inverse(cameraTransform.rotation) *
                gripWorldRotation;
            presentationRoot.localRotation = Quaternion.Slerp(
                StartLocalRotation,
                gripLocalRotation,
                easedReach);

            float reveal = SmootherRange(ReachAmount, 0f, 0.16f);
            presentationRoot.localScale =
                Vector3.one * Mathf.Lerp(0.82f, 1f, reveal);
        }

        private void ReleasePresentation()
        {
            armSubset?.Dispose();
            armSubset = null;
            if (presentationRoot != null)
            {
                GameObject rootObject = presentationRoot.gameObject;
                rootObject.SetActive(false);
                if (Application.isPlaying)
                {
                    Destroy(rootObject);
                }
                else
                {
                    DestroyImmediate(rootObject);
                }
            }

            presentationRoot = null;
            handModelRoot = null;
        }

        private static Vector3 QuadraticBezier(
            Vector3 start,
            Vector3 control,
            Vector3 end,
            float amount)
        {
            float remaining = 1f - amount;
            return remaining * remaining * start +
                   2f * remaining * amount * control +
                   amount * amount * end;
        }

        private static float SmootherRange(
            float amount,
            float start,
            float end)
        {
            float normalized = Mathf.Clamp01(
                (amount - start) / Mathf.Max(0.0001f, end - start));
            return SmootherStep(normalized);
        }

        private static float SmootherStep(float amount)
        {
            float clamped = Mathf.Clamp01(amount);
            return clamped * clamped * clamped *
                   (clamped * (clamped * 6f - 15f) + 10f);
        }
    }
}
