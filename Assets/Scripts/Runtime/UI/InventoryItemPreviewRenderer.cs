using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    /// <summary>
    /// Renders the selected inventory item as a live low-resolution 3D model
    /// without exposing the preview stage to gameplay cameras.
    /// </summary>
    [DefaultExecutionOrder(-840)]
    [DisallowMultipleComponent]
    public sealed class InventoryItemPreviewRenderer : MonoBehaviour
    {
        public const int TextureWidth = 160;
        public const int TextureHeight = 128;

        private const int PreviewLayer = 5;
        private const float RotationSpeed = 18f;
        private static readonly Vector3 StagePosition =
            new Vector3(0f, -1000f, 0f);

        private InventoryController controller;
        private Transform previewStage;
        private Transform modelPivot;
        private Transform modelRoot;
        private Camera previewCamera;
        private RenderTexture previewTexture;
        private InventoryItemId currentItemId;
        private Quaternion baseRotation = Quaternion.identity;
        private float rotationDegrees;

        public Texture Texture => previewTexture;
        public Camera PreviewCamera => previewCamera;
        public Transform ModelRoot => modelRoot;
        public InventoryItemId CurrentItemId => currentItemId;
        public float RotationDegrees => rotationDegrees;
        public bool IsRendering =>
            previewStage != null &&
            previewStage.gameObject.activeSelf &&
            previewCamera != null &&
            previewCamera.enabled;

        public void Initialize(InventoryController inventoryController)
        {
            if (inventoryController == null)
            {
                throw new ArgumentNullException(
                    nameof(inventoryController));
            }

            if (controller != null && controller != inventoryController)
            {
                throw new InvalidOperationException(
                    "The inventory preview is already initialized.");
            }

            controller = inventoryController;
        }

        public void RefreshNow()
        {
            if (controller == null ||
                !controller.IsOpen ||
                !controller.HasSelection)
            {
                SetStageVisible(false);
                return;
            }

            EnsureStage();
            InventoryItemId selected = controller.SelectedStack.ItemId;
            if (selected != currentItemId || modelRoot == null)
            {
                BuildSelectedModel(selected);
            }

            SetStageVisible(true);
        }

        public void Hide()
        {
            SetStageVisible(false);
        }

        private void LateUpdate()
        {
            RefreshNow();
            if (!IsRendering || modelPivot == null)
            {
                return;
            }

            rotationDegrees = Mathf.Repeat(
                rotationDegrees + RotationSpeed * Time.unscaledDeltaTime,
                360f);
            ApplyModelRotation();
        }

        private void EnsureStage()
        {
            if (previewStage != null)
            {
                return;
            }

            GameObject stageObject = new GameObject(
                "Inventory 3D Preview Stage");
            stageObject.hideFlags = HideFlags.HideAndDontSave;
            previewStage = stageObject.transform;
            previewStage.position = StagePosition;
            previewStage.rotation = Quaternion.identity;

            modelPivot = new GameObject(
                "Inventory 3D Preview Pivot").transform;
            modelPivot.SetParent(previewStage, false);

            previewTexture = new RenderTexture(
                TextureWidth,
                TextureHeight,
                16,
                RenderTextureFormat.ARGB32)
            {
                name = "Inventory 3D Preview",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                antiAliasing = 1
            };
            previewTexture.Create();

            GameObject cameraObject = new GameObject(
                "Inventory 3D Preview Camera");
            cameraObject.transform.SetParent(previewStage, false);
            cameraObject.transform.localPosition =
                new Vector3(0f, 0f, -2f);
            cameraObject.transform.localRotation = Quaternion.identity;
            previewCamera = cameraObject.AddComponent<Camera>();
            // An icon is a few centimetres wide through an orthographic
            // lens, so the PS1 snap grid is coarse enough here to quantize
            // a whole item onto a handful of positions. Icons stay smooth.
            cameraObject.AddComponent<Rendering.Ps1VertexJitterExclusion>();
            previewCamera.enabled = false;
            previewCamera.orthographic = true;
            previewCamera.orthographicSize = 0.40f;
            previewCamera.nearClipPlane = 0.01f;
            previewCamera.farClipPlane = 5f;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            previewCamera.cullingMask = 1 << PreviewLayer;
            previewCamera.targetTexture = previewTexture;
            previewCamera.allowHDR = false;
            previewCamera.allowMSAA = false;
            previewCamera.useOcclusionCulling = false;
            previewCamera.depthTextureMode = DepthTextureMode.None;
            UniversalAdditionalCameraData cameraData =
                previewCamera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = false;

            BuildPreviewLight(
                "Inventory 3D Preview Key Light",
                new Vector3(-1.25f, 1.45f, -1.35f),
                new Color(1f, 0.88f, 0.70f),
                2.6f);
            BuildPreviewLight(
                "Inventory 3D Preview Fill Light",
                new Vector3(1.25f, 0.55f, -0.60f),
                new Color(0.48f, 0.72f, 0.74f),
                1.15f);
            SetLayerRecursively(previewStage, PreviewLayer);
            previewStage.gameObject.SetActive(false);
        }

        private void BuildPreviewLight(
            string lightName,
            Vector3 localPosition,
            Color color,
            float intensity)
        {
            GameObject lightObject = new GameObject(lightName);
            lightObject.transform.SetParent(previewStage, false);
            lightObject.transform.localPosition = localPosition;
            Light previewLight = lightObject.AddComponent<Light>();
            previewLight.type = LightType.Point;
            previewLight.color = color;
            previewLight.intensity = intensity;
            previewLight.range = 5f;
            previewLight.shadows = LightShadows.None;
            previewLight.cullingMask = 1 << PreviewLayer;
        }

        private void BuildSelectedModel(InventoryItemId itemId)
        {
            DestroyCurrentModel();
            modelPivot.localPosition = Vector3.zero;
            modelPivot.localRotation = Quaternion.identity;
            modelPivot.localScale = Vector3.one;
            modelRoot = InventoryItemModelFactory.BuildPreviewModel(
                itemId,
                modelPivot);
            SetLayerRecursively(modelRoot, PreviewLayer);
            CenterModel();
            currentItemId = itemId;
            baseRotation = GetBaseRotation(itemId);
            rotationDegrees = 0f;
            ApplyModelRotation();
            FrameModel();
        }

        private void CenterModel()
        {
            Bounds bounds = CalculateRendererBounds(modelRoot);
            Vector3 center = modelPivot.InverseTransformPoint(bounds.center);
            modelRoot.localPosition -= center;
        }

        private void FrameModel()
        {
            Bounds bounds = CalculateRendererBounds(modelRoot);
            float radius = Mathf.Max(
                0.08f,
                bounds.extents.magnitude);
            previewCamera.orthographicSize = radius * 1.22f;
        }

        private void ApplyModelRotation()
        {
            modelPivot.localRotation =
                Quaternion.Euler(0f, rotationDegrees, 0f) *
                baseRotation;
        }

        private static Quaternion GetBaseRotation(InventoryItemId itemId)
        {
            switch (itemId)
            {
                case InventoryItemId.ApartmentKeys:
                    return Quaternion.Euler(18f, -25f, -10f);
                case InventoryItemId.Lighter:
                    return Quaternion.Euler(6f, -18f, 0f);
                case InventoryItemId.VodkaBottle:
                    return Quaternion.Euler(0f, -18f, 0f);
                case InventoryItemId.ChickenEgg:
                    return Quaternion.Euler(10f, -24f, -6f);
                case InventoryItemId.OpenStewCan:
                    return Quaternion.Euler(8f, 22f, -4f);
                case InventoryItemId.ClosedStewCan:
                    return Quaternion.Euler(8f, -20f, -3f);
                case InventoryItemId.InstantNoodles:
                    return Quaternion.Euler(24f, -18f, -8f);
                case InventoryItemId.DayOldLoaf:
                    return Quaternion.Euler(12f, -28f, -5f);
                default:
                    return Quaternion.identity;
            }
        }

        private void SetStageVisible(bool visible)
        {
            if (previewStage == null)
            {
                return;
            }

            previewStage.gameObject.SetActive(visible);
            if (previewCamera != null)
            {
                previewCamera.enabled = visible;
            }
        }

        private void DestroyCurrentModel()
        {
            currentItemId = InventoryItemId.None;
            if (modelRoot == null)
            {
                return;
            }

            modelRoot.gameObject.SetActive(false);
            DestroyPreviewObject(modelRoot.gameObject);
            modelRoot = null;
        }

        private static Bounds CalculateRendererBounds(Transform root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException(
                    "An inventory preview model requires renderers.");
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static void SetLayerRecursively(
            Transform root,
            int layer)
        {
            root.gameObject.layer = layer;
            for (int index = 0; index < root.childCount; index++)
            {
                SetLayerRecursively(root.GetChild(index), layer);
            }
        }

        private void OnDisable()
        {
            Hide();
        }

        private void OnDestroy()
        {
            DestroyCurrentModel();
            if (previewCamera != null)
            {
                previewCamera.targetTexture = null;
                previewCamera = null;
            }

            if (previewStage != null)
            {
                previewStage.gameObject.SetActive(false);
                DestroyPreviewObject(previewStage.gameObject);
                previewStage = null;
                modelPivot = null;
            }

            if (previewTexture != null)
            {
                previewTexture.Release();
                DestroyPreviewObject(previewTexture);
                previewTexture = null;
            }
        }

        private static void DestroyPreviewObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
