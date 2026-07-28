using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Casts a stable, alpha-clipped player silhouette toward the main light.
    /// The shadow-only card faces the light instead of the camera, so camera
    /// orbit cannot flatten or rotate the projected shadow.
    /// </summary>
    [DefaultExecutionOrder(220)]
    [DisallowMultipleComponent]
    public sealed class PlayerDynamicShadow : MonoBehaviour
    {
        private const float DirectionHysteresisDegrees = 2f;
        private const float DirectionEpsilon = 0.0001f;
        private const float DefaultVisualHeightOffset = 0.04f;

        private readonly List<Sprite> directionSprites =
            new List<Sprite>(PlayerSpriteRig.DirectionCount);

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
        public IReadOnlyList<Sprite> DirectionSprites => directionSprites;
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
            RefreshShadow();
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

            for (int index = 0; index < directionSprites.Count; index++)
            {
                DestroyGeneratedObject(directionSprites[index]);
            }

            directionSprites.Clear();
        }

        private void EnsureShadowExists()
        {
            if (shadowRoot != null)
            {
                return;
            }

            Texture2D atlas = Resources.Load<Texture2D>(
                PlayerSpriteRig.ReferenceAtlasResourcePath);
            ValidateAtlas(atlas);
            CreateDirectionSprites(atlas);

            GameObject shadowObject =
                new GameObject("Dynamic Player Shadow Caster");
            shadowRoot = shadowObject.transform;
            shadowRoot.SetParent(transform, false);
            shadowRenderer = shadowObject.AddComponent<SpriteRenderer>();
            shadowRenderer.sharedMaterial =
                PlayerShadowResources.ShadowCasterMaterial;
            shadowRenderer.shadowCastingMode =
                ShadowCastingMode.ShadowsOnly;
            shadowRenderer.receiveShadows = false;
            shadowRenderer.lightProbeUsage = LightProbeUsage.Off;
            shadowRenderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            shadowRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            shadowRenderer.sprite = directionSprites[0];
        }

        private void CreateDirectionSprites(Texture2D atlas)
        {
            Vector2 pivot = new Vector2(
                PlayerSpriteRig.FeetPivotXPixels /
                PlayerSpriteRig.FrameWidth,
                PlayerSpriteRig.FeetPivotPixels /
                PlayerSpriteRig.FrameHeight);
            for (int index = 0;
                 index < PlayerSpriteRig.DirectionCount;
                 index++)
            {
                Sprite sprite = Sprite.Create(
                    atlas,
                    new Rect(
                        index * PlayerSpriteRig.FrameWidth,
                        0f,
                        PlayerSpriteRig.FrameWidth,
                        PlayerSpriteRig.FrameHeight),
                    pivot,
                    PlayerSpriteRig.PixelsPerUnit,
                    0,
                    SpriteMeshType.FullRect);
                sprite.name =
                    $"PlayerShadow{(PlayerViewDirection)index}";
                sprite.hideFlags = HideFlags.DontSave;
                directionSprites.Add(sprite);
            }
        }

        private void RefreshShadow()
        {
            Light directional = ResolveMainLight();
            if (!CanCastShadow(directional))
            {
                shadowRenderer.enabled = false;
                return;
            }

            Vector3 towardLight = Vector3.ProjectOnPlane(
                -directional.transform.forward,
                Vector3.up);
            if (towardLight.sqrMagnitude < DirectionEpsilon)
            {
                shadowRenderer.enabled = false;
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
            shadowRenderer.sprite = directionSprites[(int)direction];
            shadowRenderer.enabled = true;

            Quaternion lightFacingRotation =
                Quaternion.LookRotation(towardLight, Vector3.up);
            Vector3 poseOffset = sourceVisual.PoseRoot != null
                ? sourceVisual.PoseRoot.localPosition
                : Vector3.zero;
            Quaternion poseRotation = sourceVisual.PoseRoot != null
                ? sourceVisual.PoseRoot.localRotation
                : Quaternion.identity;
            Vector3 basePosition = sourceVisual.transform != null
                ? sourceVisual.transform.position
                : facingTransform.position +
                  (Vector3.up * DefaultVisualHeightOffset);

            shadowRoot.SetPositionAndRotation(
                basePosition +
                (lightFacingRotation * new Vector3(
                    poseOffset.x,
                    poseOffset.y,
                    poseOffset.z)),
                lightFacingRotation * poseRotation);
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

        private static void ValidateAtlas(Texture2D atlas)
        {
            int expectedWidth =
                PlayerSpriteRig.FrameWidth *
                PlayerSpriteRig.DirectionCount;
            if (atlas == null)
            {
                throw new InvalidOperationException(
                    $"Player shadow atlas was not found at Resources/" +
                    $"{PlayerSpriteRig.ReferenceAtlasResourcePath}.");
            }

            if (atlas.width != expectedWidth ||
                atlas.height != PlayerSpriteRig.FrameHeight)
            {
                throw new InvalidOperationException(
                    $"Player shadow atlas must be {expectedWidth}x" +
                    $"{PlayerSpriteRig.FrameHeight}, but was " +
                    $"{atlas.width}x{atlas.height}.");
            }
        }

        private static void DestroyGeneratedObject(UnityEngine.Object value)
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
