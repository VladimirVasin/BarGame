using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Camera-local attachment rig used by the seated bar drink presentation.
    /// Its Player3D arm subsets can be hidden while their grip transforms stay
    /// active for drink-service vessel movement.
    /// </summary>
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class BarDrinkFirstPersonArms : MonoBehaviour
    {
        private const float VisibilityThreshold = 0.002f;

        private static readonly Vector3 HiddenRootPosition =
            new Vector3(0f, -0.48f, 0.08f);
        private static readonly Vector3 RightRestPosition =
            new Vector3(0.31f, -0.24f, 0.61f);
        private static readonly Vector3 RightGripPosition =
            new Vector3(0.23f, -0.16f, 0.53f);
        private static readonly Vector3 LeftRestPosition =
            new Vector3(-0.31f, -0.25f, 0.62f);
        private static readonly Vector3 LeftLiftControl =
            new Vector3(-0.22f, -0.05f, 0.50f);
        private static readonly Vector3 LeftLiftPosition =
            new Vector3(-0.10f, -0.015f, 0.43f);

        private static readonly Quaternion RightRestRotation =
            Quaternion.Euler(4f, -10f, -8f);
        private static readonly Quaternion RightGripRotation =
            Quaternion.Euler(-7f, -25f, -17f);
        private static readonly Quaternion LeftRestRotation =
            Quaternion.Euler(2f, 10f, 9f);
        private static readonly Quaternion LeftLiftRotation =
            Quaternion.Euler(-17f, 1f, 24f);
        private static readonly Vector3 GripMountPosition =
            new Vector3(0f, 0.105f, 0.072f);

        private Camera targetCamera;
        private Transform presentationRoot;
        private Transform rightArmRoot;
        private Transform leftArmRoot;
        private Transform rightBottleGripAnchor;
        private Transform leftVesselGripAnchor;
        private Player3DFirstPersonSubset rightArmSubset;
        private Player3DFirstPersonSubset leftArmSubset;
        private float poseVisibilityAmount;

        public bool IsInitialized { get; private set; }
        public bool IsVisible =>
            !VisualsSuppressed &&
            presentationRoot != null &&
            presentationRoot.gameObject.activeSelf;
        public bool VisualsSuppressed { get; private set; }
        public float VisibilityAmount { get; private set; }
        public float RightGripAmount { get; private set; }
        public float DrinkLiftAmount { get; private set; }
        public Camera TargetCamera => targetCamera;
        public Transform PresentationRoot => presentationRoot;
        public Transform Root => presentationRoot;
        public Transform RightBottleGripAnchor => rightBottleGripAnchor;
        public Transform RightGripAnchor => rightBottleGripAnchor;
        public Transform LeftVesselGripAnchor => leftVesselGripAnchor;
        public Transform LeftVesselAttachmentAnchor =>
            leftVesselGripAnchor;
        public Transform LeftGripAnchor => leftVesselGripAnchor;
        public Player3DAssetRegistry RightModelRegistry =>
            rightArmSubset?.Registry;
        public Player3DAssetRegistry LeftModelRegistry =>
            leftArmSubset?.Registry;

        public void Initialize(Camera camera)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            ReleasePresentation();
            targetCamera = camera;
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
                IsInitialized = false;
                throw;
            }
        }

        public void ApplyPresentation(
            float visibility,
            float rightGrip,
            float drinkLift,
            bool renderVisuals = true)
        {
            RequireFinite(visibility, nameof(visibility));
            RequireFinite(rightGrip, nameof(rightGrip));
            RequireFinite(drinkLift, nameof(drinkLift));

            poseVisibilityAmount = Mathf.Clamp01(visibility);
            VisualsSuppressed = !renderVisuals;
            VisibilityAmount = renderVisuals
                ? poseVisibilityAmount
                : 0f;
            RightGripAmount = Mathf.Clamp01(rightGrip);
            DrinkLiftAmount = Mathf.Clamp01(drinkLift);
            if (!IsInitialized || presentationRoot == null)
            {
                return;
            }

            SetArmRenderersEnabled(renderVisuals);
            bool shouldBeActive =
                poseVisibilityAmount > VisibilityThreshold &&
                targetCamera != null;
            presentationRoot.gameObject.SetActive(shouldBeActive);
            if (shouldBeActive)
            {
                RefreshPose();
            }
        }

        public void Hide()
        {
            poseVisibilityAmount = 0f;
            VisibilityAmount = 0f;
            RightGripAmount = 0f;
            DrinkLiftAmount = 0f;
            VisualsSuppressed = false;
            SetArmRenderersEnabled(true);
            if (presentationRoot == null)
            {
                return;
            }

            RefreshPose();
            presentationRoot.gameObject.SetActive(false);
        }

        public void ResetPresentation()
        {
            Hide();
        }

        public void Reset()
        {
            Hide();
        }

        private void LateUpdate()
        {
            if (presentationRoot == null ||
                !presentationRoot.gameObject.activeSelf)
            {
                return;
            }

            if (targetCamera == null)
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
            IsInitialized = false;
        }

        private void BuildPresentation()
        {
            presentationRoot =
                new GameObject("Bar Drink First Person Arms").transform;
            presentationRoot.SetParent(targetCamera.transform, false);
            presentationRoot.gameObject.layer =
                targetCamera.gameObject.layer;
            presentationRoot.gameObject.SetActive(false);

            rightArmRoot = CreateArmRoot("Right Arm");
            leftArmRoot = CreateArmRoot("Left Arm");
            Transform rightModelMount = CreateModelMount(
                rightArmRoot,
                "Right Arm Model Mount");
            Transform leftModelMount = CreateModelMount(
                leftArmRoot,
                "Left Arm Model Mount");
            rightArmSubset = Player3DFirstPersonSubset.Create(
                rightModelMount,
                Player3DFirstPersonSide.Right,
                targetCamera.gameObject.layer,
                "Player3D Right Arm Subset");
            leftArmSubset = Player3DFirstPersonSubset.Create(
                leftModelMount,
                Player3DFirstPersonSide.Left,
                targetCamera.gameObject.layer,
                "Player3D Left Arm Subset");

            rightBottleGripAnchor = rightArmSubset.SourceGrip;
            leftVesselGripAnchor = leftArmSubset.SourceGrip;
        }

        private Transform CreateArmRoot(string objectName)
        {
            Transform result = new GameObject(objectName).transform;
            result.SetParent(presentationRoot, false);
            result.gameObject.layer = targetCamera.gameObject.layer;
            return result;
        }

        private Transform CreateModelMount(
            Transform parent,
            string objectName)
        {
            Transform result = new GameObject(objectName).transform;
            result.SetParent(parent, false);
            result.gameObject.layer = targetCamera.gameObject.layer;
            result.localPosition = GripMountPosition;
            result.localRotation = Quaternion.identity;
            return result;
        }

        private void RefreshPose()
        {
            if (presentationRoot == null || targetCamera == null)
            {
                return;
            }

            if (presentationRoot.parent != targetCamera.transform)
            {
                presentationRoot.SetParent(targetCamera.transform, false);
            }

            float reveal = SmootherStep(poseVisibilityAmount);
            float grip = SmootherStep(RightGripAmount);
            float lift = SmootherStep(DrinkLiftAmount);

            presentationRoot.localPosition = Vector3.LerpUnclamped(
                HiddenRootPosition,
                Vector3.zero,
                reveal);
            presentationRoot.localRotation = Quaternion.identity;
            presentationRoot.localScale = Vector3.one;

            rightArmRoot.localPosition = Vector3.LerpUnclamped(
                RightRestPosition,
                RightGripPosition,
                grip);
            rightArmRoot.localRotation = Quaternion.SlerpUnclamped(
                RightRestRotation,
                RightGripRotation,
                grip);
            rightArmRoot.localScale = Vector3.one;

            leftArmRoot.localPosition = QuadraticBezier(
                LeftRestPosition,
                LeftLiftControl,
                LeftLiftPosition,
                lift);
            leftArmRoot.localRotation = Quaternion.SlerpUnclamped(
                LeftRestRotation,
                LeftLiftRotation,
                lift);
            leftArmRoot.localScale = Vector3.one;
        }

        private void SetArmRenderersEnabled(bool enabled)
        {
            SetSubsetRenderersEnabled(rightArmSubset, enabled);
            SetSubsetRenderersEnabled(leftArmSubset, enabled);
        }

        private static void SetSubsetRenderersEnabled(
            Player3DFirstPersonSubset subset,
            bool enabled)
        {
            if (subset == null)
            {
                return;
            }

            foreach (Renderer renderer in subset.VisibleRenderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = enabled;
                }
            }
        }

        private void ReleasePresentation()
        {
            rightArmSubset?.Dispose();
            leftArmSubset?.Dispose();
            rightArmSubset = null;
            leftArmSubset = null;

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
            rightArmRoot = null;
            leftArmRoot = null;
            rightBottleGripAnchor = null;
            leftVesselGripAnchor = null;
            poseVisibilityAmount = 0f;
            VisibilityAmount = 0f;
            RightGripAmount = 0f;
            DrinkLiftAmount = 0f;
            VisualsSuppressed = false;
        }

        private static void RequireFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Presentation values must be finite.");
            }
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

        private static float SmootherStep(float amount)
        {
            float clamped = Mathf.Clamp01(amount);
            return clamped * clamped * clamped *
                   (clamped * (clamped * 6f - 15f) + 10f);
        }
    }
}
