using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Casts a light-facing articulated copy of the player puppet.
    /// The copy follows the actor and the live joint pose without depending
    /// on the camera-facing presentation hierarchy.
    /// </summary>
    [DefaultExecutionOrder(220)]
    [DisallowMultipleComponent]
    public sealed class PlayerDynamicShadow : MonoBehaviour
    {
        private const float DirectionHysteresisDegrees = 2f;
        private const float DirectionEpsilon = 0.0001f;
        private const float DefaultVisualHeightOffset = 0.005f;

        private static readonly int[] ParentPartIndices =
        {
            -1,
            -1,
            (int)PlayerPuppetPart.LeftUpperArm,
            -1,
            (int)PlayerPuppetPart.RightUpperArm,
            -1,
            (int)PlayerPuppetPart.LeftUpperLeg,
            -1,
            (int)PlayerPuppetPart.RightUpperLeg
        };

        private readonly List<Sprite> directionSprites =
            new List<Sprite>(PlayerSpriteRig.DirectionCount);
        private readonly List<SpriteRenderer> partRenderers =
            new List<SpriteRenderer>(PlayerSpriteRig.PartCount);
        private readonly Transform[] partTransforms =
            new Transform[PlayerSpriteRig.PartCount];

        private Transform facingTransform;
        private PlayerSpriteRig sourceVisual;
        private Light mainLight;
        private Transform shadowRoot;
        private SpriteRenderer shadowRenderer;
        private PlayerViewDirectionSelector directionSelector;

        public bool IsInitialized { get; private set; }
        public PlayerViewDirection CurrentDirection =>
            directionSelector != null
                ? directionSelector.CurrentDirection
                : PlayerViewDirection.Front;
        public IReadOnlyList<Sprite> DirectionSprites =>
            directionSprites;
        public IReadOnlyList<SpriteRenderer> Renderers =>
            partRenderers;
        public Transform ShadowRoot => shadowRoot;
        public SpriteRenderer Renderer => shadowRenderer;
        public Light MainLight => mainLight;

        public void Initialize(
            Transform playerFacingTransform,
            PlayerSpriteRig visual,
            Light directionalLight = null)
        {
            facingTransform = playerFacingTransform != null
                ? playerFacingTransform
                : throw new ArgumentNullException(
                    nameof(playerFacingTransform));
            sourceVisual = visual != null
                ? visual
                : throw new ArgumentNullException(nameof(visual));
            mainLight = directionalLight;

            EnsureShadowExists();
            directionSelector = new PlayerViewDirectionSelector(
                DirectionHysteresisDegrees,
                PlayerViewDirection.Front);
            IsInitialized = true;
            if (isActiveAndEnabled)
            {
                RefreshShadow();
            }
            else
            {
                SetRenderersEnabled(false);
            }
        }

        public Transform GetPartTransform(PlayerPuppetPart part)
        {
            ValidatePart(part);
            return partTransforms[(int)part];
        }

        public SpriteRenderer GetPartRenderer(PlayerPuppetPart part)
        {
            ValidatePart(part);
            int index = (int)part;
            return index < partRenderers.Count
                ? partRenderers[index]
                : null;
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
            SetRenderersEnabled(false);
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
            if (shadowRoot != null)
            {
                DestroyGeneratedObject(shadowRoot.gameObject);
                shadowRoot = null;
                shadowRenderer = null;
            }

            directionSprites.Clear();
            partRenderers.Clear();
            Array.Clear(
                partTransforms,
                0,
                partTransforms.Length);
        }

        private void EnsureShadowExists()
        {
            if (shadowRoot != null)
            {
                return;
            }

            CacheBodyDirectionSprites();

            GameObject shadowObject =
                new GameObject("Dynamic Player Shadow Caster");
            shadowRoot = shadowObject.transform;
            shadowRoot.SetParent(transform, false);

            for (int partIndex = 0;
                 partIndex < PlayerSpriteRig.PartCount;
                 partIndex++)
            {
                PlayerPuppetPart part =
                    (PlayerPuppetPart)partIndex;
                GameObject partObject =
                    new GameObject($"Shadow {part}");
                Transform partTransform = partObject.transform;
                int parentIndex = ParentPartIndices[partIndex];
                Transform parent = parentIndex >= 0
                    ? partTransforms[parentIndex]
                    : shadowRoot;
                partTransform.SetParent(parent, false);
                partTransforms[partIndex] = partTransform;

                SpriteRenderer renderer =
                    partObject.AddComponent<SpriteRenderer>();
                ConfigureRenderer(renderer);
                partRenderers.Add(renderer);
            }

            shadowRenderer =
                partRenderers[(int)PlayerPuppetPart.Body];
        }

        private void CacheBodyDirectionSprites()
        {
            directionSprites.Clear();
            for (int directionIndex = 0;
                 directionIndex < PlayerSpriteRig.DirectionCount;
                 directionIndex++)
            {
                directionSprites.Add(
                    sourceVisual.GetPartSprite(
                        PlayerPuppetPart.Body,
                        (PlayerViewDirection)directionIndex));
            }
        }

        private static void ConfigureRenderer(
            SpriteRenderer renderer)
        {
            renderer.sharedMaterial =
                PlayerShadowResources.ShadowCasterMaterial;
            renderer.shadowCastingMode =
                ShadowCastingMode.ShadowsOnly;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.Object;
        }

        private void RefreshShadow()
        {
            Light directional = ResolveMainLight();
            if (!CanCastShadow(directional))
            {
                SetRenderersEnabled(false);
                return;
            }

            Vector3 towardLight = Vector3.ProjectOnPlane(
                -directional.transform.forward,
                Vector3.up);
            if (towardLight.sqrMagnitude < DirectionEpsilon)
            {
                SetRenderersEnabled(false);
                return;
            }

            towardLight.Normalize();
            Vector3 actorForward = Vector3.ProjectOnPlane(
                facingTransform.forward,
                Vector3.up);
            if (actorForward.sqrMagnitude < DirectionEpsilon)
            {
                actorForward = Vector3.forward;
            }

            float signedAngle = Vector3.SignedAngle(
                actorForward.normalized,
                towardLight,
                Vector3.up);
            PlayerViewDirection direction =
                directionSelector.Select(signedAngle);
            Quaternion lightFacingRotation =
                Quaternion.LookRotation(towardLight, Vector3.up);
            Vector3 poseOffset = sourceVisual.PoseRoot != null
                ? sourceVisual.PoseRoot.localPosition
                : Vector3.zero;
            Quaternion poseRotation =
                sourceVisual.PoseRoot != null
                    ? sourceVisual.PoseRoot.localRotation
                    : Quaternion.identity;
            Vector3 basePosition = sourceVisual.transform != null
                ? sourceVisual.transform.position
                : facingTransform.position +
                  (Vector3.up * DefaultVisualHeightOffset);

            shadowRoot.SetPositionAndRotation(
                basePosition +
                (lightFacingRotation * poseOffset),
                lightFacingRotation * poseRotation);
            shadowRoot.localScale = Vector3.one;
            SynchronizeParts(direction);
        }

        private void SynchronizeParts(
            PlayerViewDirection direction)
        {
            if (sourceVisual.IsDetailedFallActive)
            {
                SynchronizeDetailedFall(direction);
                return;
            }

            for (int partIndex = 0;
                 partIndex < PlayerSpriteRig.PartCount;
                 partIndex++)
            {
                PlayerPuppetPart part =
                    (PlayerPuppetPart)partIndex;
                Transform sourceTransform =
                    sourceVisual.GetPartTransform(part);
                Transform shadowTransform =
                    partTransforms[partIndex];
                shadowTransform.localPosition =
                    sourceVisual.GetPartPoseLocalPosition(
                        part,
                        direction);
                shadowTransform.localRotation =
                    sourceVisual.GetPartPoseLocalRotation(
                        part,
                        direction);
                shadowTransform.localScale =
                    sourceTransform != null
                        ? sourceTransform.localScale
                        : Vector3.one;

                SpriteRenderer renderer =
                    partRenderers[partIndex];
                if (renderer.sharedMaterial == null)
                {
                    renderer.sharedMaterial =
                        PlayerShadowResources.ShadowCasterMaterial;
                }

                renderer.sprite =
                    sourceVisual.GetPartSprite(part, direction);
                renderer.enabled = true;
            }
        }

        private void SynchronizeDetailedFall(
            PlayerViewDirection direction)
        {
            for (int partIndex = 0;
                 partIndex < PlayerSpriteRig.PartCount;
                 partIndex++)
            {
                Transform shadowTransform =
                    partTransforms[partIndex];
                shadowTransform.localPosition = Vector3.zero;
                shadowTransform.localRotation = Quaternion.identity;
                shadowTransform.localScale = Vector3.one;

                SpriteRenderer renderer =
                    partRenderers[partIndex];
                if (renderer.sharedMaterial == null)
                {
                    renderer.sharedMaterial =
                        PlayerShadowResources.ShadowCasterMaterial;
                }

                bool isBody =
                    partIndex == (int)PlayerPuppetPart.Body;
                renderer.enabled = isBody;
                if (isBody)
                {
                    renderer.sprite =
                        sourceVisual.GetDetailedFallSprite(
                            direction,
                            sourceVisual.FallDirection,
                            sourceVisual.DetailedFallFrameIndex);
                }
            }
        }

        private void SetRenderersEnabled(bool value)
        {
            for (int index = 0;
                 index < partRenderers.Count;
                 index++)
            {
                if (partRenderers[index] != null)
                {
                    partRenderers[index].enabled = value;
                }
            }
        }

        private Light ResolveMainLight()
        {
            if (mainLight != null)
            {
                return mainLight;
            }

            mainLight = RenderSettings.sun;
            return mainLight;
        }

        private static bool CanCastShadow(Light light)
        {
            return light != null &&
                   light.type == LightType.Directional &&
                   light.isActiveAndEnabled &&
                   light.shadows != LightShadows.None;
        }

        private static void ValidatePart(PlayerPuppetPart part)
        {
            int index = (int)part;
            if (index < 0 ||
                index >= PlayerSpriteRig.PartCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(part),
                    part,
                    "Part must be one of the nine puppet layers.");
            }
        }

        private static void DestroyGeneratedObject(
            UnityEngine.Object value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(value);
            }
            else
            {
                DestroyImmediate(value);
            }
        }
    }
}
