using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Temporarily replaces the articulated player visual with a camera-plane
    /// frame sequence while leaving the physical player root at its stand
    /// position.
    /// </summary>
    [DefaultExecutionOrder(205)]
    [DisallowMultipleComponent]
    public sealed class PlayerAnimatedInteractionController :
        MonoBehaviour
    {
        public const int AtlasColumnCount = 8;
        public const int AtlasRowCount = 8;
        public const int AtlasFrameCount =
            AtlasColumnCount * AtlasRowCount;
        public const int FrameWidth = 128;
        public const int FrameHeight = 96;
        public const float PixelsPerUnit = 48f;
        public const float HipPivotXPixels = 64f;
        public const float HipPivotYPixels = 40f;
        public const bool AuthoredTextureFlipX = true;

        private readonly List<Sprite> generatedSprites =
            new List<Sprite>(AtlasFrameCount);
        private readonly bool[] previousRigRendererStates =
            new bool[PlayerSpriteRig.PartCount];

        private PlayerRuntime player;
        private Camera targetCamera;
        private PlayerAnimatedInteractionTimeline timeline;
        private Transform animationRoot;
        private Transform animationVisualRoot;
        private SpriteRenderer animationRenderer;
        private BillboardSprite billboard;
        private Texture2D loadedAtlas;
        private string loadedResourcePath;
        private Vector3 standHip;
        private Vector3 actionHip;
        private Vector3 actionRightAxis;
        private bool hasActionRightAxis;
        private bool stateCaptured;
        private bool previousMotorInput;
        private bool previousInteractorInput;
        private bool previousDynamicShadowEnabled;
        private bool previousContactShadowEnabled;

        public event Action<PlayerAnimatedInteractionPhase> PhaseChanged;

        public bool IsInitialized { get; private set; }
        public PlayerAnimatedInteractionPhase Phase =>
            timeline != null
                ? timeline.Phase
                : PlayerAnimatedInteractionPhase.Idle;
        public int FrameIndex =>
            timeline != null ? timeline.FrameIndex : -1;
        public bool IsActive =>
            timeline != null && timeline.IsActive;
        public float ExitDurationMultiplier =>
            timeline != null
                ? timeline.ExitDurationMultiplier
                : 1f;
        public double ExitDurationSeconds =>
            timeline != null
                ? timeline.ExitDurationSeconds
                : 0d;
        public SpriteRenderer AnimationRenderer =>
            animationRenderer;
        public Transform AnimationVisualRoot =>
            animationVisualRoot;
        public bool HasActionRightAxis =>
            hasActionRightAxis;
        public Vector3 ActionRightAxis =>
            actionRightAxis;
        public float TargetCameraPlaneRollDegrees
        {
            get;
            private set;
        }
        public float CurrentCameraPlaneRollDegrees
        {
            get;
            private set;
        }

        public void Initialize(
            PlayerRuntime playerRuntime,
            Camera camera)
        {
            ValidatePlayerRuntime(playerRuntime);
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            CompleteInteraction();
            ReleasePresentation();

            player = playerRuntime;
            targetCamera = camera;
            EnsurePresentationExists();
            IsInitialized = true;
        }

        public bool Begin(
            PlayerAnimatedInteractionDefinition definition,
            Vector3 standHipPosition,
            Vector3 actionHipPosition)
        {
            return Begin(
                definition,
                standHipPosition,
                actionHipPosition,
                Vector3.zero);
        }

        public bool Begin(
            PlayerAnimatedInteractionDefinition definition,
            Vector3 standHipPosition,
            Vector3 actionHipPosition,
            Vector3 worldActionRightAxis)
        {
            return BeginInternal(
                definition,
                standHipPosition,
                actionHipPosition,
                worldActionRightAxis,
                false);
        }

        public bool BeginLooping(
            PlayerAnimatedInteractionDefinition definition,
            Vector3 standHipPosition,
            Vector3 actionHipPosition)
        {
            return BeginLooping(
                definition,
                standHipPosition,
                actionHipPosition,
                Vector3.zero);
        }

        public bool BeginLooping(
            PlayerAnimatedInteractionDefinition definition,
            Vector3 standHipPosition,
            Vector3 actionHipPosition,
            Vector3 worldActionRightAxis)
        {
            return BeginInternal(
                definition,
                standHipPosition,
                actionHipPosition,
                worldActionRightAxis,
                true);
        }

        private bool BeginInternal(
            PlayerAnimatedInteractionDefinition definition,
            Vector3 standHipPosition,
            Vector3 actionHipPosition,
            Vector3 worldActionRightAxis,
            bool startLooping)
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException(
                    "Initialize the animated interaction controller first.");
            }

            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (!isActiveAndEnabled || IsActive)
            {
                return false;
            }

            ValidateAnchors(
                standHipPosition,
                actionHipPosition);
            ValidateActionRightAxis(
                worldActionRightAxis,
                nameof(worldActionRightAxis),
                out bool useActionRightAxis,
                out Vector3 normalizedActionRightAxis);
            PrepareFrames(definition);
            ConfigureMaterial(definition);

            PlayerAnimatedInteractionTimeline nextTimeline =
                new PlayerAnimatedInteractionTimeline(definition);
            if (startLooping)
            {
                nextTimeline.BeginLooping();
            }
            else
            {
                nextTimeline.Begin();
            }

            standHip = standHipPosition;
            actionHip = actionHipPosition;
            hasActionRightAxis = useActionRightAxis;
            actionRightAxis = normalizedActionRightAxis;
            CaptureAndHidePlayerState();
            timeline = nextTimeline;
            ApplyInputForPhase(Phase);
            animationRenderer.enabled = true;
            ApplyCurrentPresentation();
            PhaseChanged?.Invoke(Phase);
            return true;
        }

        public bool RequestExit()
        {
            return RequestExit(1f);
        }

        public bool RequestExit(float durationMultiplier)
        {
            if (timeline == null ||
                !timeline.RequestExit(durationMultiplier))
            {
                return false;
            }

            ApplyInputForPhase(Phase);
            ApplyCurrentPresentation();
            PhaseChanged?.Invoke(Phase);
            return true;
        }

        public bool CancelActiveInteraction()
        {
            if (!IsActive && !stateCaptured)
            {
                return false;
            }

            CompleteInteraction();
            return true;
        }

        /// <summary>
        /// Maps logical frames from the atlas's lower PNG row upward because
        /// Unity sprite texture rectangles use a bottom-left origin.
        /// </summary>
        public static Rect GetAtlasFrameRect(int frameIndex)
        {
            if (frameIndex < 0 ||
                frameIndex >= AtlasFrameCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(frameIndex),
                    frameIndex,
                    $"Frame index must be between 0 and " +
                    $"{AtlasFrameCount - 1}.");
            }

            int column = frameIndex % AtlasColumnCount;
            int rowFromBottom =
                frameIndex / AtlasColumnCount;
            return new Rect(
                column * FrameWidth,
                rowFromBottom * FrameHeight,
                FrameWidth,
                FrameHeight);
        }

        /// <summary>
        /// Returns the local Z roll for the flipped visual child that makes
        /// authored texture +X follow a world-space axis on the camera plane.
        /// </summary>
        public static float CalculateCameraPlaneTargetRollDegrees(
            Vector3 worldActionRightAxis,
            Vector3 cameraRight,
            Vector3 cameraUp)
        {
            ValidateFiniteVector(
                worldActionRightAxis,
                nameof(worldActionRightAxis));
            ValidateFiniteVector(
                cameraRight,
                nameof(cameraRight));
            ValidateFiniteVector(
                cameraUp,
                nameof(cameraUp));

            if (worldActionRightAxis.sqrMagnitude <= 0.000001f)
            {
                return 0f;
            }

            if (cameraRight.sqrMagnitude <= 0.000001f)
            {
                throw new ArgumentException(
                    "Camera right must have a usable direction.",
                    nameof(cameraRight));
            }

            Vector3 normalizedCameraRight =
                cameraRight.normalized;
            Vector3 orthogonalCameraUp =
                Vector3.ProjectOnPlane(
                    cameraUp,
                    normalizedCameraRight);
            if (orthogonalCameraUp.sqrMagnitude <= 0.000001f)
            {
                throw new ArgumentException(
                    "Camera up must not be parallel to camera right.",
                    nameof(cameraUp));
            }

            orthogonalCameraUp.Normalize();
            float screenRight = Vector3.Dot(
                worldActionRightAxis,
                normalizedCameraRight);
            float screenUp = Vector3.Dot(
                worldActionRightAxis,
                orthogonalCameraUp);
            if ((screenRight * screenRight) +
                (screenUp * screenUp) <= 0.000001f)
            {
                return 0f;
            }

            float authoredScreenAngle =
                Mathf.Atan2(screenUp, screenRight) *
                Mathf.Rad2Deg;
            return -authoredScreenAngle;
        }

        /// <summary>
        /// Projects the action axis through its authored world anchor so a
        /// perspective camera's exact screen-space line determines the roll.
        /// A basis projection is used when the samples are behind the camera
        /// or collapse to one screen point.
        /// </summary>
        public static float CalculateCameraPlaneTargetRollDegrees(
            Camera camera,
            Vector3 worldAnchor,
            Vector3 worldActionRightAxis)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            ValidateFiniteVector(
                worldAnchor,
                nameof(worldAnchor));
            ValidateFiniteVector(
                worldActionRightAxis,
                nameof(worldActionRightAxis));
            if (worldActionRightAxis.sqrMagnitude <= 0.000001f)
            {
                return 0f;
            }

            Vector3 normalizedAxis =
                worldActionRightAxis.normalized;
            Vector3 screenStart =
                camera.WorldToScreenPoint(
                    worldAnchor - (normalizedAxis * 0.5f));
            Vector3 screenEnd =
                camera.WorldToScreenPoint(
                    worldAnchor + (normalizedAxis * 0.5f));
            Vector2 screenDelta = new Vector2(
                screenEnd.x - screenStart.x,
                screenEnd.y - screenStart.y);
            bool canUsePerspectiveLine =
                IsFinite(screenStart) &&
                IsFinite(screenEnd) &&
                screenStart.z > 0f &&
                screenEnd.z > 0f &&
                screenDelta.sqrMagnitude > 0.0001f;
            if (canUsePerspectiveLine)
            {
                float authoredScreenAngle =
                    Mathf.Atan2(
                        screenDelta.y,
                        screenDelta.x) *
                    Mathf.Rad2Deg;
                return -authoredScreenAngle;
            }

            return CalculateCameraPlaneTargetRollDegrees(
                normalizedAxis,
                camera.transform.right,
                camera.transform.up);
        }

        public static float EvaluateCameraPlaneRollDegrees(
            PlayerAnimatedInteractionPhase phase,
            float phaseProgress,
            float targetRollDegrees)
        {
            if (!IsFinite(phaseProgress))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(phaseProgress),
                    phaseProgress,
                    "Phase progress must be finite.");
            }

            if (!IsFinite(targetRollDegrees))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetRollDegrees),
                    targetRollDegrees,
                    "Target roll must be finite.");
            }

            float easedProgress =
                SmoothProgress(phaseProgress);
            switch (phase)
            {
                case PlayerAnimatedInteractionPhase.Entering:
                    return targetRollDegrees * easedProgress;
                case PlayerAnimatedInteractionPhase.Looping:
                    return targetRollDegrees;
                case PlayerAnimatedInteractionPhase.Exiting:
                    return targetRollDegrees *
                           (1f - easedProgress);
                default:
                    return 0f;
            }
        }

        private void Update()
        {
            if (!IsActive)
            {
                return;
            }

            PlayerAnimatedInteractionPhase previousPhase =
                timeline.Phase;
            timeline.Advance(Time.deltaTime);
            if (!timeline.IsActive)
            {
                CompleteInteraction();
                return;
            }

            ApplyCurrentPresentation();
            if (timeline.Phase == previousPhase)
            {
                return;
            }

            ApplyInputForPhase(timeline.Phase);
            PhaseChanged?.Invoke(timeline.Phase);
        }

        private void OnDisable()
        {
            CompleteInteraction();
        }

        private void OnDestroy()
        {
            CompleteInteraction();
            ReleasePresentation();
            PhaseChanged = null;
        }

        private void CaptureAndHidePlayerState()
        {
            previousMotorInput = player.Motor.InputEnabled;
            previousInteractorInput = player.Interactor.InputEnabled;
            previousDynamicShadowEnabled =
                player.Shadow != null && player.Shadow.enabled;
            previousContactShadowEnabled =
                player.ContactShadow != null &&
                player.ContactShadow.enabled;

            IReadOnlyList<SpriteRenderer> renderers =
                player.Visual.Renderers;
            for (int index = 0;
                 index < PlayerSpriteRig.PartCount;
                 index++)
            {
                SpriteRenderer renderer =
                    index < renderers.Count
                        ? renderers[index]
                        : null;
                previousRigRendererStates[index] =
                    renderer != null && renderer.enabled;
                if (renderer != null)
                {
                    renderer.enabled = false;
                }
            }

            player.Motor.SetInputEnabled(false);
            player.Interactor.SetInputEnabled(false);
            if (player.Shadow != null)
            {
                player.Shadow.enabled = false;
            }

            if (player.ContactShadow != null)
            {
                player.ContactShadow.enabled = false;
            }

            stateCaptured = true;
        }

        private void RestorePlayerState()
        {
            if (!stateCaptured)
            {
                return;
            }

            if (player.Visual != null)
            {
                IReadOnlyList<SpriteRenderer> renderers =
                    player.Visual.Renderers;
                for (int index = 0;
                     index < PlayerSpriteRig.PartCount;
                     index++)
                {
                    SpriteRenderer renderer =
                        index < renderers.Count
                            ? renderers[index]
                            : null;
                    if (renderer != null)
                    {
                        renderer.enabled =
                            previousRigRendererStates[index];
                    }
                }
            }

            if (player.Shadow != null)
            {
                player.Shadow.enabled =
                    previousDynamicShadowEnabled;
            }

            if (player.ContactShadow != null)
            {
                player.ContactShadow.enabled =
                    previousContactShadowEnabled;
            }

            player.Motor?.SetInputEnabled(previousMotorInput);
            player.Interactor?.SetInputEnabled(
                previousInteractorInput);
            stateCaptured = false;
        }

        private void ApplyInputForPhase(
            PlayerAnimatedInteractionPhase phase)
        {
            if (!stateCaptured)
            {
                return;
            }

            player.Motor?.SetInputEnabled(false);
            bool allowInteraction =
                phase == PlayerAnimatedInteractionPhase.Looping &&
                previousInteractorInput;
            player.Interactor?.SetInputEnabled(allowInteraction);
        }

        private void ApplyCurrentPresentation()
        {
            if (animationRoot == null ||
                animationRenderer == null ||
                timeline == null ||
                timeline.FrameIndex < 0 ||
                timeline.FrameIndex >= generatedSprites.Count)
            {
                return;
            }

            animationRoot.position = GetCurrentHipPosition();
            billboard?.FaceCameraNow();
            ApplyCameraPlaneRoll();
            animationRenderer.sprite =
                generatedSprites[timeline.FrameIndex];
            if (!animationRenderer.enabled)
            {
                animationRenderer.enabled = true;
            }
        }

        private void ApplyCameraPlaneRoll()
        {
            TargetCameraPlaneRollDegrees = 0f;
            Camera camera = targetCamera != null
                ? targetCamera
                : Camera.main;
            if (hasActionRightAxis && camera != null)
            {
                TargetCameraPlaneRollDegrees =
                    CalculateCameraPlaneTargetRollDegrees(
                        camera,
                        actionHip,
                        actionRightAxis);
            }

            CurrentCameraPlaneRollDegrees =
                EvaluateCameraPlaneRollDegrees(
                    timeline.Phase,
                    timeline.PhaseProgress,
                    TargetCameraPlaneRollDegrees);
            if (animationVisualRoot != null)
            {
                animationVisualRoot.localRotation =
                    Quaternion.Euler(
                        0f,
                        0f,
                        CurrentCameraPlaneRollDegrees);
            }
        }

        private Vector3 GetCurrentHipPosition()
        {
            switch (timeline.Phase)
            {
                case PlayerAnimatedInteractionPhase.Entering:
                    return Vector3.LerpUnclamped(
                        standHip,
                        actionHip,
                        SmoothProgress(timeline.PhaseProgress));
                case PlayerAnimatedInteractionPhase.Looping:
                    return actionHip;
                case PlayerAnimatedInteractionPhase.Exiting:
                    return Vector3.LerpUnclamped(
                        actionHip,
                        standHip,
                        SmoothProgress(timeline.PhaseProgress));
                default:
                    return standHip;
            }
        }

        private void CompleteInteraction()
        {
            bool shouldNotify =
                (timeline != null && timeline.IsActive) ||
                stateCaptured;
            timeline?.Reset();
            if (animationRenderer != null)
            {
                animationRenderer.enabled = false;
                animationRenderer.sprite = null;
            }

            ResetOrientation();
            RestorePlayerState();
            if (shouldNotify)
            {
                PhaseChanged?.Invoke(
                    PlayerAnimatedInteractionPhase.Idle);
            }
        }

        private void EnsurePresentationExists()
        {
            if (animationRoot != null)
            {
                billboard.Initialize(targetCamera);
                billboard.SetCameraPlaneAlignment(true);
                return;
            }

            GameObject animationObject =
                new GameObject(
                    "Animated Interaction Billboard Root");
            animationRoot = animationObject.transform;
            animationRoot.SetParent(
                player.GameObject.transform,
                false);

            GameObject visualObject =
                new GameObject(
                    "Animated Interaction Camera-Plane Visual");
            animationVisualRoot = visualObject.transform;
            animationVisualRoot.SetParent(
                animationRoot,
                false);
            animationRenderer =
                visualObject.AddComponent<SpriteRenderer>();
            ConfigureRenderer(animationRenderer);
            billboard = animationObject.AddComponent<BillboardSprite>();
            billboard.Initialize(targetCamera);
            billboard.SetCameraPlaneAlignment(true);
            animationRenderer.enabled = false;
        }

        private void ConfigureRenderer(
            SpriteRenderer renderer)
        {
            SpriteRenderer bodyRenderer =
                player.Visual.BodyRenderer;
            if (bodyRenderer != null)
            {
                renderer.sharedMaterial =
                    bodyRenderer.sharedMaterial;
                renderer.sortingLayerID =
                    bodyRenderer.sortingLayerID;
                renderer.sortingOrder =
                    bodyRenderer.sortingOrder + 10;
                renderer.color = bodyRenderer.color;
            }
            else
            {
                renderer.color = Color.white;
                renderer.sortingOrder = 10;
            }

            renderer.flipX = AuthoredTextureFlipX;
            renderer.flipY = false;
            renderer.spriteSortPoint = SpriteSortPoint.Pivot;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
        }

        private void ConfigureMaterial(
            PlayerAnimatedInteractionDefinition definition)
        {
            if (animationRenderer == null)
            {
                return;
            }

            animationRenderer.sharedMaterial =
                definition.RenderAboveSceneDepth
                    ? PlayerAnimatedInteractionResources
                        .OverlayMaterial
                    : player.Visual.BodyRenderer
                        .sharedMaterial;
        }

        private void PrepareFrames(
            PlayerAnimatedInteractionDefinition definition)
        {
            if (definition.TotalFrameCount > AtlasFrameCount)
            {
                throw new ArgumentException(
                    $"The sequence uses {definition.TotalFrameCount} " +
                    $"frames, but the interaction atlas contains only " +
                    $"{AtlasFrameCount}.",
                    nameof(definition));
            }

            if (loadedAtlas != null &&
                loadedResourcePath ==
                definition.TextureResourcePath &&
                generatedSprites.Count == AtlasFrameCount)
            {
                return;
            }

            DestroyGeneratedSprites();
            loadedAtlas = Resources.Load<Texture2D>(
                definition.TextureResourcePath);
            ValidateAtlas(
                loadedAtlas,
                definition.TextureResourcePath);
            loadedAtlas.filterMode = FilterMode.Point;
            loadedAtlas.wrapMode = TextureWrapMode.Clamp;
            loadedResourcePath = definition.TextureResourcePath;

            Vector2 normalizedPivot = new Vector2(
                HipPivotXPixels / FrameWidth,
                HipPivotYPixels / FrameHeight);
            for (int frameIndex = 0;
                 frameIndex < AtlasFrameCount;
                 frameIndex++)
            {
                Sprite sprite = Sprite.Create(
                    loadedAtlas,
                    GetAtlasFrameRect(frameIndex),
                    normalizedPivot,
                    PixelsPerUnit,
                    0,
                    SpriteMeshType.FullRect);
                sprite.name =
                    $"PlayerAnimatedInteractionFrame{frameIndex:00}";
                sprite.hideFlags = HideFlags.DontSave;
                generatedSprites.Add(sprite);
            }
        }

        private void ReleasePresentation()
        {
            DestroyGeneratedSprites();
            loadedAtlas = null;
            loadedResourcePath = null;
            if (animationRoot != null)
            {
                DestroyGeneratedObject(animationRoot.gameObject);
            }

            animationRoot = null;
            animationVisualRoot = null;
            animationRenderer = null;
            billboard = null;
        }

        private void DestroyGeneratedSprites()
        {
            if (animationRenderer != null)
            {
                animationRenderer.sprite = null;
            }

            for (int index = 0;
                 index < generatedSprites.Count;
                 index++)
            {
                DestroyGeneratedObject(generatedSprites[index]);
            }

            generatedSprites.Clear();
        }

        private static void ValidatePlayerRuntime(
            PlayerRuntime playerRuntime)
        {
            if (playerRuntime.GameObject == null)
            {
                throw new ArgumentException(
                    "The player runtime has no GameObject.",
                    nameof(playerRuntime));
            }

            if (playerRuntime.Motor == null ||
                playerRuntime.Interactor == null ||
                playerRuntime.Visual == null)
            {
                throw new ArgumentException(
                    "The player runtime must contain a motor, " +
                    "interactor and sprite rig.",
                    nameof(playerRuntime));
            }
        }

        private static void ValidateAtlas(
            Texture2D atlas,
            string resourcePath)
        {
            if (atlas == null)
            {
                throw new InvalidOperationException(
                    $"Animated interaction atlas was not found at " +
                    $"Resources/{resourcePath}.");
            }

            int expectedWidth =
                AtlasColumnCount * FrameWidth;
            int expectedHeight =
                AtlasRowCount * FrameHeight;
            if (atlas.width != expectedWidth ||
                atlas.height != expectedHeight)
            {
                throw new InvalidOperationException(
                    $"Animated interaction atlas at Resources/" +
                    $"{resourcePath} must be {expectedWidth}x" +
                    $"{expectedHeight}, but is {atlas.width}x" +
                    $"{atlas.height}.");
            }
        }

        private static void ValidateAnchors(
            Vector3 standHipPosition,
            Vector3 actionHipPosition)
        {
            if (!IsFinite(standHipPosition))
            {
                throw new ArgumentException(
                    "The stand hip position must be finite.",
                    nameof(standHipPosition));
            }

            if (!IsFinite(actionHipPosition))
            {
                throw new ArgumentException(
                    "The action hip position must be finite.",
                    nameof(actionHipPosition));
            }
        }

        private static void ValidateActionRightAxis(
            Vector3 value,
            string parameterName,
            out bool hasAxis,
            out Vector3 normalizedAxis)
        {
            ValidateFiniteVector(
                value,
                parameterName);
            hasAxis =
                value.sqrMagnitude > 0.000001f;
            normalizedAxis = hasAxis
                ? value.normalized
                : Vector3.zero;
        }

        private static void ValidateFiniteVector(
            Vector3 value,
            string parameterName)
        {
            if (!IsFinite(value))
            {
                throw new ArgumentException(
                    "The vector must be finite.",
                    parameterName);
            }
        }

        private void ResetOrientation()
        {
            actionRightAxis = Vector3.zero;
            hasActionRightAxis = false;
            TargetCameraPlaneRollDegrees = 0f;
            CurrentCameraPlaneRollDegrees = 0f;
            if (animationVisualRoot != null)
            {
                animationVisualRoot.localRotation =
                    Quaternion.identity;
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }

        private static float SmoothProgress(float progress)
        {
            return Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(progress));
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

    public static class PlayerAnimatedInteractionResources
    {
        public const string OverlayShaderResourcePath =
            "Shaders/PlayerAnimatedInteractionOverlay";

        private static Material overlayMaterial;

        public static Material OverlayMaterial
        {
            get
            {
                if (overlayMaterial == null)
                {
                    Shader shader = Resources.Load<Shader>(
                        OverlayShaderResourcePath);
                    if (shader == null)
                    {
                        throw new InvalidOperationException(
                            $"Missing Resources shader " +
                            $"'{OverlayShaderResourcePath}'.");
                    }

                    overlayMaterial = new Material(shader)
                    {
                        name =
                            "Player Animated Interaction Overlay Shared",
                        hideFlags = HideFlags.HideAndDontSave,
                        enableInstancing = true
                    };
                }

                return overlayMaterial;
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            if (overlayMaterial == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(overlayMaterial);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(overlayMaterial);
            }

            overlayMaterial = null;
        }
    }
}
