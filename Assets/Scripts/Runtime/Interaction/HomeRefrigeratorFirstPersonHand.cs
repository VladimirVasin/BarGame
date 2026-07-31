using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Camera-local, runtime-composed hand used while the refrigerator owns
    /// the Home camera. The grip is evaluated from the live handle transform,
    /// so the hand keeps following it while the door swings.
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

        private static readonly Color SleeveColor =
            new Color(0.17f, 0.18f, 0.12f);
        private static readonly Color SleeveShadowColor =
            new Color(0.085f, 0.09f, 0.06f);
        private static readonly Color CuffColor =
            new Color(0.27f, 0.25f, 0.17f);
        private static readonly Color SkinColor =
            new Color(0.50f, 0.35f, 0.25f);
        private static readonly Color SkinHighlightColor =
            new Color(0.62f, 0.44f, 0.31f);
        private static readonly Color NailColor =
            new Color(0.51f, 0.39f, 0.33f);

        private readonly Transform[] proximalFingerPivots =
            new Transform[4];
        private readonly Transform[] distalFingerPivots =
            new Transform[4];

        private Camera targetCamera;
        private Transform handleTarget;
        private Transform presentationRoot;
        private Transform handModelRoot;
        private Transform thumbPivot;

        public bool IsInitialized { get; private set; }
        public bool IsVisible =>
            presentationRoot != null &&
            presentationRoot.gameObject.activeSelf;
        public float ReachAmount { get; private set; }
        public Camera TargetCamera => targetCamera;
        public Transform HandleTarget => handleTarget;
        public Transform PresentationRoot => presentationRoot;
        public Transform HandModelRoot => handModelRoot;

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
            BuildPresentation();
            IsInitialized = true;
            ResetPresentation();
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

            ApplyFingerCurl(0f);
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

            handModelRoot =
                new GameObject("Hand Model").transform;
            handModelRoot.SetParent(presentationRoot, false);

            BuildSleeve();
            BuildHand();
            presentationRoot.gameObject.SetActive(false);
        }

        private void BuildSleeve()
        {
            CreateCylinder(
                "First Person Sleeve",
                handModelRoot,
                new Vector3(0f, -0.185f, 0.002f),
                new Vector3(0.124f, 0.17f, 0.112f),
                SleeveColor);
            GameObject sleeveShadow = CreateBox(
                "First Person Sleeve Seam",
                handModelRoot,
                new Vector3(0.047f, -0.18f, -0.047f),
                new Vector3(0.014f, 0.28f, 0.008f),
                SleeveShadowColor);
            sleeveShadow.transform.localRotation =
                Quaternion.Euler(0f, 0f, -2f);

            CreateCylinder(
                "First Person Sleeve Cuff",
                handModelRoot,
                new Vector3(0f, -0.025f, 0f),
                new Vector3(0.142f, 0.035f, 0.126f),
                CuffColor);
            CreateCylinder(
                "First Person Wrist",
                handModelRoot,
                new Vector3(0f, 0.018f, 0f),
                new Vector3(0.102f, 0.048f, 0.09f),
                SkinColor);
        }

        private void BuildHand()
        {
            CreateBox(
                "First Person Palm",
                handModelRoot,
                new Vector3(0f, 0.078f, 0f),
                new Vector3(0.112f, 0.132f, 0.046f),
                SkinColor);
            CreateBox(
                "First Person Knuckles",
                handModelRoot,
                new Vector3(0f, 0.137f, -0.006f),
                new Vector3(0.118f, 0.025f, 0.052f),
                SkinHighlightColor);

            for (int index = 0; index < 4; index++)
            {
                float x = -0.045f + index * 0.03f;
                float length = index == 0 || index == 3
                    ? 0.048f
                    : 0.055f;
                BuildFinger(index, x, length);
            }

            thumbPivot =
                new GameObject("First Person Thumb Pivot").transform;
            thumbPivot.SetParent(handModelRoot, false);
            thumbPivot.localPosition =
                new Vector3(-0.062f, 0.072f, -0.002f);
            CreateBox(
                "First Person Thumb",
                thumbPivot,
                new Vector3(-0.025f, 0.018f, -0.003f),
                new Vector3(0.064f, 0.031f, 0.038f),
                SkinHighlightColor);
        }

        private void BuildFinger(
            int index,
            float x,
            float proximalLength)
        {
            Transform proximal =
                new GameObject(
                    $"First Person Finger {index + 1} Proximal Pivot")
                    .transform;
            proximal.SetParent(handModelRoot, false);
            proximal.localPosition = new Vector3(x, 0.144f, 0f);
            proximalFingerPivots[index] = proximal;

            CreateBox(
                $"First Person Finger {index + 1} Proximal",
                proximal,
                new Vector3(0f, proximalLength * 0.5f, 0f),
                new Vector3(0.024f, proximalLength, 0.036f),
                SkinColor);

            Transform distal =
                new GameObject(
                    $"First Person Finger {index + 1} Distal Pivot")
                    .transform;
            distal.SetParent(proximal, false);
            distal.localPosition =
                new Vector3(0f, proximalLength, 0f);
            distalFingerPivots[index] = distal;

            float distalLength = proximalLength * 0.70f;
            CreateBox(
                $"First Person Finger {index + 1} Distal",
                distal,
                new Vector3(0f, distalLength * 0.5f, 0f),
                new Vector3(0.023f, distalLength, 0.034f),
                SkinHighlightColor);
            CreateBox(
                $"First Person Finger {index + 1} Nail",
                distal,
                new Vector3(
                    0f,
                    distalLength * 0.73f,
                    0.018f),
                new Vector3(0.014f, distalLength * 0.34f, 0.004f),
                NailColor);
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

            Vector3 gripWorldPosition =
                handleTarget.position +
                towardCamera * GripForwardOffset -
                targetUp * GripVerticalOffset;
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

            Quaternion gripWorldRotation =
                Quaternion.LookRotation(towardCamera, targetUp);
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
            ApplyFingerCurl(
                SmootherRange(ReachAmount, 0.52f, 1f));
        }

        private void ApplyFingerCurl(float amount)
        {
            float curl = Mathf.Clamp01(amount);
            for (int index = 0;
                 index < proximalFingerPivots.Length;
                 index++)
            {
                if (proximalFingerPivots[index] != null)
                {
                    float spread = (index - 1.5f) * (1f - curl) * 2.2f;
                    proximalFingerPivots[index].localRotation =
                        Quaternion.Euler(-27f * curl, 0f, spread);
                }

                if (distalFingerPivots[index] != null)
                {
                    distalFingerPivots[index].localRotation =
                        Quaternion.Euler(-53f * curl, 0f, 0f);
                }
            }

            if (thumbPivot != null)
            {
                thumbPivot.localRotation = Quaternion.Euler(
                    -9f - 29f * curl,
                    7f * curl,
                    -35f + 18f * curl);
            }
        }

        private GameObject CreateBox(
            string objectName,
            Transform parent,
            Vector3 localPosition,
            Vector3 size,
            Color color)
        {
            GameObject result = RuntimePrimitiveFactory.CreateBox(
                objectName,
                parent,
                localPosition,
                size,
                color,
                false);
            ConfigureRenderer(result);
            return result;
        }

        private GameObject CreateCylinder(
            string objectName,
            Transform parent,
            Vector3 localPosition,
            Vector3 size,
            Color color)
        {
            GameObject result = RuntimePrimitiveFactory.CreateCylinder(
                objectName,
                parent,
                localPosition,
                size,
                color,
                false);
            ConfigureRenderer(result);
            return result;
        }

        private void ConfigureRenderer(GameObject part)
        {
            part.layer = targetCamera.gameObject.layer;
            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void ReleasePresentation()
        {
            if (presentationRoot != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(presentationRoot.gameObject);
                }
                else
                {
                    DestroyImmediate(presentationRoot.gameObject);
                }
            }

            presentationRoot = null;
            handModelRoot = null;
            thumbPivot = null;
            for (int index = 0;
                 index < proximalFingerPivots.Length;
                 index++)
            {
                proximalFingerPivots[index] = null;
                distalFingerPivots[index] = null;
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
