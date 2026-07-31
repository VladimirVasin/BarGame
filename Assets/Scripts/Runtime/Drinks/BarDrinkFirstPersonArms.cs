using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Runtime-composed, camera-local arms used by the seated bar drink
    /// presentation. Bottle and vessel visuals remain owned by the drink
    /// service; this component only supplies animated attachment anchors.
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

        private readonly Transform[] rightProximalFingerPivots =
            new Transform[4];
        private readonly Transform[] rightDistalFingerPivots =
            new Transform[4];
        private readonly Transform[] leftProximalFingerPivots =
            new Transform[4];
        private readonly Transform[] leftDistalFingerPivots =
            new Transform[4];

        private Camera targetCamera;
        private Transform presentationRoot;
        private Transform rightArmRoot;
        private Transform leftArmRoot;
        private Transform rightThumbPivot;
        private Transform leftThumbPivot;
        private Transform rightBottleGripAnchor;
        private Transform leftVesselGripAnchor;

        public bool IsInitialized { get; private set; }
        public bool IsVisible =>
            presentationRoot != null &&
            presentationRoot.gameObject.activeSelf;
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

        public void Initialize(Camera camera)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            ReleasePresentation();
            targetCamera = camera;
            BuildPresentation();
            IsInitialized = true;
            ResetPresentation();
        }

        public void ApplyPresentation(
            float visibility,
            float rightGrip,
            float drinkLift)
        {
            RequireFinite(visibility, nameof(visibility));
            RequireFinite(rightGrip, nameof(rightGrip));
            RequireFinite(drinkLift, nameof(drinkLift));

            VisibilityAmount = Mathf.Clamp01(visibility);
            RightGripAmount = Mathf.Clamp01(rightGrip);
            DrinkLiftAmount = Mathf.Clamp01(drinkLift);
            if (!IsInitialized || presentationRoot == null)
            {
                return;
            }

            bool shouldBeVisible =
                VisibilityAmount > VisibilityThreshold &&
                targetCamera != null;
            presentationRoot.gameObject.SetActive(shouldBeVisible);
            if (shouldBeVisible)
            {
                RefreshPose();
            }
        }

        public void Hide()
        {
            VisibilityAmount = 0f;
            RightGripAmount = 0f;
            DrinkLiftAmount = 0f;
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
            if (!IsVisible)
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

            rightArmRoot = new GameObject("Right Arm").transform;
            rightArmRoot.SetParent(presentationRoot, false);
            rightArmRoot.gameObject.layer = targetCamera.gameObject.layer;

            leftArmRoot = new GameObject("Left Arm").transform;
            leftArmRoot.SetParent(presentationRoot, false);
            leftArmRoot.gameObject.layer = targetCamera.gameObject.layer;

            BuildArm(
                rightArmRoot,
                true,
                rightProximalFingerPivots,
                rightDistalFingerPivots,
                out rightThumbPivot,
                out rightBottleGripAnchor,
                "Bottle Grip Anchor");
            BuildArm(
                leftArmRoot,
                false,
                leftProximalFingerPivots,
                leftDistalFingerPivots,
                out leftThumbPivot,
                out leftVesselGripAnchor,
                "Vessel Grip Anchor");

            presentationRoot.gameObject.SetActive(false);
        }

        private void BuildArm(
            Transform armRoot,
            bool isRight,
            Transform[] proximalPivots,
            Transform[] distalPivots,
            out Transform thumbPivot,
            out Transform attachmentAnchor,
            string anchorName)
        {
            float side = isRight ? 1f : -1f;
            CreateCylinderSegment(
                "Low Poly Sleeve",
                armRoot,
                new Vector3(side * 0.20f, -0.39f, 0.075f),
                new Vector3(side * 0.018f, -0.045f, 0.012f),
                0.132f,
                SleeveColor);
            CreateCylinderSegment(
                "Sleeve Shadow Panel",
                armRoot,
                new Vector3(side * 0.155f, -0.36f, 0.023f),
                new Vector3(side * 0.040f, -0.09f, -0.025f),
                0.052f,
                SleeveShadowColor);
            CreateCylinderSegment(
                "Sleeve Cuff",
                armRoot,
                new Vector3(side * 0.036f, -0.085f, 0.014f),
                new Vector3(side * 0.006f, -0.025f, 0.004f),
                0.148f,
                CuffColor);
            CreateCylinderSegment(
                "Wrist",
                armRoot,
                new Vector3(side * 0.008f, -0.035f, 0.003f),
                new Vector3(0f, 0.025f, 0f),
                0.102f,
                SkinColor);

            CreateBox(
                "Palm",
                armRoot,
                new Vector3(0f, 0.083f, 0f),
                new Vector3(0.116f, 0.132f, 0.052f),
                SkinColor);
            CreateBox(
                "Knuckles",
                armRoot,
                new Vector3(0f, 0.142f, -0.006f),
                new Vector3(0.122f, 0.027f, 0.057f),
                SkinHighlightColor);

            for (int index = 0; index < 4; index++)
            {
                float x = -0.045f + index * 0.03f;
                float length = index == 0 || index == 3
                    ? 0.048f
                    : 0.055f;
                BuildFinger(
                    armRoot,
                    index,
                    x,
                    length,
                    proximalPivots,
                    distalPivots);
            }

            thumbPivot = new GameObject("Thumb Pivot").transform;
            thumbPivot.SetParent(armRoot, false);
            thumbPivot.gameObject.layer = targetCamera.gameObject.layer;
            thumbPivot.localPosition =
                new Vector3(-side * 0.064f, 0.077f, -0.002f);
            CreateBox(
                "Thumb",
                thumbPivot,
                new Vector3(-side * 0.025f, 0.019f, -0.003f),
                new Vector3(0.064f, 0.032f, 0.039f),
                SkinHighlightColor);

            attachmentAnchor = new GameObject(anchorName).transform;
            attachmentAnchor.SetParent(armRoot, false);
            attachmentAnchor.gameObject.layer =
                targetCamera.gameObject.layer;
            attachmentAnchor.localPosition =
                new Vector3(0f, 0.105f, 0.072f);
            attachmentAnchor.localRotation = Quaternion.identity;
        }

        private void BuildFinger(
            Transform armRoot,
            int index,
            float x,
            float proximalLength,
            Transform[] proximalPivots,
            Transform[] distalPivots)
        {
            Transform proximal =
                new GameObject(
                    $"Finger {index + 1} Proximal Pivot").transform;
            proximal.SetParent(armRoot, false);
            proximal.gameObject.layer = targetCamera.gameObject.layer;
            proximal.localPosition = new Vector3(x, 0.148f, 0f);
            proximalPivots[index] = proximal;

            CreateBox(
                $"Finger {index + 1} Proximal",
                proximal,
                new Vector3(0f, proximalLength * 0.5f, 0f),
                new Vector3(0.024f, proximalLength, 0.038f),
                SkinColor);

            Transform distal =
                new GameObject(
                    $"Finger {index + 1} Distal Pivot").transform;
            distal.SetParent(proximal, false);
            distal.gameObject.layer = targetCamera.gameObject.layer;
            distal.localPosition =
                new Vector3(0f, proximalLength, 0f);
            distalPivots[index] = distal;

            float distalLength = proximalLength * 0.70f;
            CreateBox(
                $"Finger {index + 1} Distal",
                distal,
                new Vector3(0f, distalLength * 0.5f, 0f),
                new Vector3(0.023f, distalLength, 0.035f),
                SkinHighlightColor);
            CreateBox(
                $"Finger {index + 1} Nail",
                distal,
                new Vector3(0f, distalLength * 0.73f, 0.0185f),
                new Vector3(0.014f, distalLength * 0.34f, 0.004f),
                NailColor);
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

            float reveal = SmootherStep(VisibilityAmount);
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

            ApplyFingerCurl(
                rightProximalFingerPivots,
                rightDistalFingerPivots,
                rightThumbPivot,
                true,
                Mathf.Lerp(0.08f, 0.92f, grip));
            ApplyFingerCurl(
                leftProximalFingerPivots,
                leftDistalFingerPivots,
                leftThumbPivot,
                false,
                Mathf.Lerp(0.60f, 0.82f, lift));
        }

        private static void ApplyFingerCurl(
            Transform[] proximalPivots,
            Transform[] distalPivots,
            Transform thumbPivot,
            bool isRight,
            float amount)
        {
            float curl = Mathf.Clamp01(amount);
            for (int index = 0; index < proximalPivots.Length; index++)
            {
                if (proximalPivots[index] != null)
                {
                    float spread =
                        (index - 1.5f) * (1f - curl) * 2.2f;
                    proximalPivots[index].localRotation =
                        Quaternion.Euler(-29f * curl, 0f, spread);
                }

                if (distalPivots[index] != null)
                {
                    distalPivots[index].localRotation =
                        Quaternion.Euler(-55f * curl, 0f, 0f);
                }
            }

            if (thumbPivot == null)
            {
                return;
            }

            float side = isRight ? 1f : -1f;
            thumbPivot.localRotation = Quaternion.Euler(
                -9f - 31f * curl,
                side * 7f * curl,
                side * (-35f + 18f * curl));
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

        private GameObject CreateCylinderSegment(
            string objectName,
            Transform parent,
            Vector3 start,
            Vector3 end,
            float width,
            Color color)
        {
            Vector3 direction = end - start;
            float length = direction.magnitude;
            GameObject result = RuntimePrimitiveFactory.CreateCylinder(
                objectName,
                parent,
                (start + end) * 0.5f,
                new Vector3(width, length * 0.5f, width),
                color,
                false);
            if (length > 0.0001f)
            {
                result.transform.localRotation =
                    Quaternion.FromToRotation(
                        Vector3.up,
                        direction / length);
            }

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
                presentationRoot.gameObject.SetActive(false);
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
            rightArmRoot = null;
            leftArmRoot = null;
            rightThumbPivot = null;
            leftThumbPivot = null;
            rightBottleGripAnchor = null;
            leftVesselGripAnchor = null;
            ClearFingerPivots(
                rightProximalFingerPivots,
                rightDistalFingerPivots);
            ClearFingerPivots(
                leftProximalFingerPivots,
                leftDistalFingerPivots);
        }

        private static void ClearFingerPivots(
            Transform[] proximalPivots,
            Transform[] distalPivots)
        {
            for (int index = 0; index < proximalPivots.Length; index++)
            {
                proximalPivots[index] = null;
                distalPivots[index] = null;
            }
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
