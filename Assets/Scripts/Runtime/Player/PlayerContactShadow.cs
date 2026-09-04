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

        private const float GroundProbeStartHeight = 0.45f;
        private const float GroundProbeDistance = 1.4f;

        private static readonly RaycastHit[] GroundHits =
            new RaycastHit[12];

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");

        private Transform groundTransform;
        private IPlayerPresentation sourcePresentation;
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
            IPlayerPresentation visual)
        {
            groundTransform = actorGroundTransform != null
                ? actorGroundTransform
                : throw new ArgumentNullException(
                    nameof(actorGroundTransform));
            sourcePresentation = visual != null
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

            PlayerPresentationMetrics metrics =
                sourcePresentation.Metrics;
            float plant = metrics.FootPlantAmount;
            float width = BaseWidth * Mathf.Lerp(0.94f, 1f, plant);
            float depth = BaseDepth * Mathf.Lerp(0.9f, 1f, plant);
            float alpha = Mathf.Lerp(0.36f, 0.46f, plant);
            float fall = metrics.FallAmount;
            width = Mathf.Lerp(width, 1.35f, fall);
            depth = Mathf.Lerp(depth, 0.42f, fall);
            alpha = Mathf.Lerp(alpha, 0.5f, fall);

            ResolveGroundPose(
                out Vector3 groundPosition,
                out Vector3 groundNormal);
            Vector3 referenceForward = Vector3.ProjectOnPlane(
                groundTransform.forward,
                groundNormal);
            if (referenceForward.sqrMagnitude <= 0.0001f)
            {
                referenceForward = Vector3.Cross(
                    groundTransform.right,
                    groundNormal);
            }

            referenceForward.Normalize();
            Vector3 offset = Vector3.zero;
            if (fall > 0.001f)
            {
                // The shadow slides the way the body is going — the
                // topple's own axis, which need not be sideways.
                Vector3 fallAxis = Vector3.ProjectOnPlane(
                    metrics.FallAxis,
                    groundNormal);
                if (fallAxis.sqrMagnitude <= 0.0001f)
                {
                    fallAxis = Vector3.Cross(
                        groundNormal,
                        referenceForward) * metrics.FallDirection;
                }

                fallAxis.Normalize();
                offset = fallAxis * fall * 0.24f;
            }

            shadowRoot.SetPositionAndRotation(
                groundPosition + groundNormal * GroundOffset + offset,
                Quaternion.LookRotation(
                    referenceForward,
                    groundNormal));

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

        private void ResolveGroundPose(
            out Vector3 position,
            out Vector3 normal)
        {
            position = groundTransform.position;
            normal = Vector3.up;
            Vector3 origin = position +
                             Vector3.up * GroundProbeStartHeight;
            int count = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                GroundHits,
                GroundProbeDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            float closestDistance = float.PositiveInfinity;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = GroundHits[index];
                if (hit.collider == null ||
                    hit.collider.transform.IsChildOf(groundTransform) ||
                    hit.normal.y <= 0.001f ||
                    hit.distance >= closestDistance)
                {
                    continue;
                }

                closestDistance = hit.distance;
                position = hit.point;
                normal = hit.normal.normalized;
            }
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
