using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Draws a small ambient-contact patch at the controller's ground origin.
    /// It follows the actor root but deliberately ignores puppet bob and sway.
    /// </summary>
    [DefaultExecutionOrder(225)]
    [DisallowMultipleComponent]
    public sealed class PlayerContactShadow : MonoBehaviour
    {
        public const float GroundOffset = 0.009f;
        public const float BaseWidth = 0.58f;
        public const float BaseDepth = 0.28f;

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");

        private Transform groundTransform;
        private PlayerSpriteRig sourceVisual;
        private Transform shadowRoot;
        private MeshFilter meshFilter;
        private MeshRenderer shadowRenderer;
        private MaterialPropertyBlock properties;

        public bool IsInitialized { get; private set; }
        public Transform ShadowRoot => shadowRoot;
        public MeshFilter Filter => meshFilter;
        public MeshRenderer Renderer => shadowRenderer;

        public void Initialize(
            Transform actorGroundTransform,
            PlayerSpriteRig visual)
        {
            groundTransform = actorGroundTransform != null
                ? actorGroundTransform
                : throw new ArgumentNullException(
                    nameof(actorGroundTransform));
            sourceVisual = visual != null
                ? visual
                : throw new ArgumentNullException(nameof(visual));

            EnsureShadowExists();
            IsInitialized = true;
            if (isActiveAndEnabled)
            {
                RefreshShadow();
            }
            else
            {
                shadowRenderer.enabled = false;
            }
        }

        private void OnEnable()
        {
            if (IsInitialized)
            {
                RefreshShadow();
            }
        }

        private void OnDisable()
        {
            if (shadowRenderer != null)
            {
                shadowRenderer.enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (IsInitialized)
            {
                RefreshShadow();
            }
        }

        private void OnDestroy()
        {
            if (shadowRoot == null)
            {
                return;
            }

            DestroyGeneratedObject(shadowRoot.gameObject);
            shadowRoot = null;
            meshFilter = null;
            shadowRenderer = null;
        }

        private void EnsureShadowExists()
        {
            if (shadowRoot != null)
            {
                return;
            }

            GameObject shadowObject =
                new GameObject("Player Ground Contact Shadow");
            shadowRoot = shadowObject.transform;
            shadowRoot.SetParent(groundTransform, false);
            meshFilter = shadowObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = PlayerShadowResources.ContactShadowMesh;
            shadowRenderer = shadowObject.AddComponent<MeshRenderer>();
            shadowRenderer.sharedMaterial =
                PlayerShadowResources.ContactShadowMaterial;
            shadowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            shadowRenderer.receiveShadows = false;
            shadowRenderer.lightProbeUsage = LightProbeUsage.Off;
            shadowRenderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            shadowRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            shadowRenderer.sortingOrder = -20;
        }

        private void RefreshShadow()
        {
            if (meshFilter.sharedMesh == null)
            {
                meshFilter.sharedMesh =
                    PlayerShadowResources.ContactShadowMesh;
            }

            if (shadowRenderer.sharedMaterial == null)
            {
                shadowRenderer.sharedMaterial =
                    PlayerShadowResources.ContactShadowMaterial;
            }

            float plant = Mathf.Clamp01(sourceVisual.FootPlantAmount);
            float width = BaseWidth * Mathf.Lerp(0.94f, 1f, plant);
            float depth = BaseDepth * Mathf.Lerp(0.9f, 1f, plant);
            float alpha = Mathf.Lerp(0.36f, 0.46f, plant);

            shadowRoot.localPosition =
                new Vector3(0f, GroundOffset, 0f);
            shadowRoot.localRotation = Quaternion.identity;
            shadowRoot.localScale = new Vector3(width, 1f, depth);

            if (properties == null)
            {
                properties = new MaterialPropertyBlock();
            }

            properties.Clear();
            properties.SetColor(
                BaseColorId,
                new Color(0.018f, 0.024f, 0.021f, alpha));
            shadowRenderer.SetPropertyBlock(properties);
            shadowRenderer.enabled = true;
        }

        private static void DestroyGeneratedObject(
            UnityEngine.Object generatedObject)
        {
            if (generatedObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(generatedObject);
            }
            else
            {
                DestroyImmediate(generatedObject);
            }
        }
    }
}
