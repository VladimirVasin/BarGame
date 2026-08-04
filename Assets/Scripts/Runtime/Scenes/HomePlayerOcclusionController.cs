using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class HomeOcclusionResources
    {
        public const string DitherMaterialResourcePath =
            "Materials/HomeOccluderDither";

        private static Material ditherMaterial;

        public static Material DitherMaterial
        {
            get
            {
                if (ditherMaterial == null)
                {
                    ditherMaterial = Resources.Load<Material>(
                        DitherMaterialResourcePath);
                }

                if (ditherMaterial == null ||
                    ditherMaterial.shader == null ||
                    !ditherMaterial.shader.isSupported)
                {
                    throw new InvalidOperationException(
                        "Missing or unsupported Home occluder material " +
                        $"'{DitherMaterialResourcePath}'.");
                }

                return ditherMaterial;
            }
        }
    }

    /// <summary>
    /// Reveals the Home player by dithering explicitly registered foreground
    /// renderer groups. Physical colliders and GameObject state are untouched.
    /// </summary>
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class HomePlayerOcclusionController : MonoBehaviour
    {
        private sealed class RendererState
        {
            public RendererState(
                Renderer renderer,
                Material originalMaterial)
            {
                Renderer = renderer;
                OriginalMaterial = originalMaterial;
            }

            public Renderer Renderer { get; }
            public Material OriginalMaterial { get; }
        }

        private sealed class GroupState
        {
            public GroupState(
                HomeOccluderGroup group,
                RendererState[] renderers)
            {
                Group = group;
                Renderers = renderers;
            }

            public HomeOccluderGroup Group { get; }
            public RendererState[] Renderers { get; }
            public float Visibility { get; set; } = 1f;
            public float ClearElapsedSeconds { get; set; }
            public bool Occluded { get; set; }
        }

        public const string VisibilityPropertyName =
            "_HomeOcclusionVisibility";
        public const float FadeOutSeconds = 0.15f;
        public const float ClearHoldSeconds = 0.12f;
        public const float RestoreSeconds = 0.30f;

        private static readonly int VisibilityId =
            Shader.PropertyToID(VisibilityPropertyName);

        private readonly Vector3[] playerSamples =
            new Vector3[HomeOcclusionResolver.PlayerSampleCount];
        private readonly List<GroupState> groupStates =
            new List<GroupState>();

        private MaterialPropertyBlock propertyBlock;

        private HomeInteriorRoot home;
        private Camera targetCamera;
        private IReadOnlyList<Renderer> playerRenderers;
        private HomeOcclusionRegistry registry;
        private HomeFixedCameraController fixedCamera;
        private Material ditherMaterial;
        private bool requireHomeGameplayState;

        public bool IsInitialized { get; private set; }
        public static int VisibilityPropertyId => VisibilityId;

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        public void Initialize(
            HomeInteriorRoot homeRoot,
            Camera camera,
            IPlayerPresentation playerVisual,
            HomeOcclusionRegistry occlusionRegistry,
            HomeFixedCameraController fixedCameraController)
        {
            if (homeRoot == null)
            {
                throw new ArgumentNullException(nameof(homeRoot));
            }

            if (playerVisual == null)
            {
                throw new ArgumentNullException(nameof(playerVisual));
            }

            if (fixedCameraController == null)
            {
                throw new ArgumentNullException(
                    nameof(fixedCameraController));
            }

            InitializeCore(
                homeRoot,
                camera,
                playerVisual.Renderers,
                occlusionRegistry,
                fixedCameraController,
                true);
        }

        /// <summary>
        /// Presentation-only overload for deterministic synthetic scenes.
        /// It exercises the same renderer/material path without requiring a
        /// fully composed Home root or camera-state owner.
        /// </summary>
        public void Initialize(
            Camera camera,
            IReadOnlyList<Renderer> visiblePlayerRenderers,
            HomeOcclusionRegistry occlusionRegistry)
        {
            InitializeCore(
                null,
                camera,
                visiblePlayerRenderers,
                occlusionRegistry,
                null,
                false);
        }

        public void ReevaluateImmediately()
        {
            if (!IsInitialized)
            {
                return;
            }

            if (ShouldSuspend() ||
                !TryBuildPlayerSamples())
            {
                ClearOcclusion();
                return;
            }

            for (int index = 0;
                 index < groupStates.Count;
                 index++)
            {
                GroupState state = groupStates[index];
                bool occluded = IsGroupOccluding(state);
                state.Occluded = occluded;
                state.ClearElapsedSeconds = 0f;
                SetVisibility(
                    state,
                    occluded
                        ? state.Group.MinimumVisibility
                        : 1f,
                    true);
            }
        }

        public void RefreshImmediate()
        {
            ReevaluateImmediately();
        }

        public void ClearOcclusion()
        {
            for (int index = 0;
                 index < groupStates.Count;
                 index++)
            {
                GroupState state = groupStates[index];
                state.Occluded = false;
                state.ClearElapsedSeconds = 0f;
                SetVisibility(state, 1f, false);
            }
        }

        public float GetVisibility(
            HomeOccluderGroup group)
        {
            if (group == null)
            {
                throw new ArgumentNullException(nameof(group));
            }

            for (int index = 0;
                 index < groupStates.Count;
                 index++)
            {
                GroupState state = groupStates[index];
                if (ReferenceEquals(state.Group, group))
                {
                    return state.Visibility;
                }
            }

            throw new KeyNotFoundException(
                $"Home occluder group '{group.Id}' is not controlled here.");
        }

        public float GetVisibility(string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                throw new ArgumentException(
                    "A Home occluder group id is required.",
                    nameof(groupId));
            }

            for (int index = 0;
                 index < groupStates.Count;
                 index++)
            {
                GroupState state = groupStates[index];
                if (string.Equals(
                        state.Group.Id,
                        groupId,
                        StringComparison.Ordinal))
                {
                    return state.Visibility;
                }
            }

            throw new KeyNotFoundException(
                $"Home occluder group '{groupId}' is not controlled here.");
        }

        private void InitializeCore(
            HomeInteriorRoot homeRoot,
            Camera camera,
            IReadOnlyList<Renderer> visiblePlayerRenderers,
            HomeOcclusionRegistry occlusionRegistry,
            HomeFixedCameraController fixedCameraController,
            bool enforceHomeGameplayState)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            if (visiblePlayerRenderers == null)
            {
                throw new ArgumentNullException(
                    nameof(visiblePlayerRenderers));
            }

            if (visiblePlayerRenderers.Count == 0)
            {
                throw new ArgumentException(
                    "At least one player renderer is required.",
                    nameof(visiblePlayerRenderers));
            }

            if (occlusionRegistry == null)
            {
                throw new ArgumentNullException(
                    nameof(occlusionRegistry));
            }

            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }

            RestoreOriginalMaterials();
            groupStates.Clear();

            home = homeRoot;
            targetCamera = camera;
            playerRenderers = visiblePlayerRenderers;
            registry = occlusionRegistry;
            fixedCamera = fixedCameraController;
            requireHomeGameplayState = enforceHomeGameplayState;
            ditherMaterial = HomeOcclusionResources.DitherMaterial;

            IReadOnlyList<HomeOccluderGroup> groups =
                registry.Groups;
            for (int groupIndex = 0;
                 groupIndex < groups.Count;
                 groupIndex++)
            {
                HomeOccluderGroup group = groups[groupIndex];
                var rendererStates =
                    new RendererState[group.Renderers.Count];
                for (int rendererIndex = 0;
                     rendererIndex < group.Renderers.Count;
                     rendererIndex++)
                {
                    Renderer renderer =
                        group.Renderers[rendererIndex];
                    rendererStates[rendererIndex] =
                        new RendererState(
                            renderer,
                            renderer.sharedMaterial);
                    renderer.sharedMaterial = ditherMaterial;
                }

                GroupState state =
                    new GroupState(group, rendererStates);
                groupStates.Add(state);
                SetVisibility(state, 1f, true);
            }

            IsInitialized = true;
            ReevaluateImmediately();
        }

        private void LateUpdate()
        {
            if (!IsInitialized)
            {
                return;
            }

            if (ShouldSuspend() ||
                !TryBuildPlayerSamples())
            {
                ClearOcclusion();
                return;
            }

            float deltaTime = Mathf.Max(
                0f,
                Time.unscaledDeltaTime);
            for (int index = 0;
                 index < groupStates.Count;
                 index++)
            {
                UpdateGroup(
                    groupStates[index],
                    deltaTime);
            }
        }

        private void UpdateGroup(
            GroupState state,
            float deltaTime)
        {
            bool occluded = IsGroupOccluding(state);
            state.Occluded = occluded;
            if (occluded)
            {
                state.ClearElapsedSeconds = 0f;
                float fadeRange =
                    1f - state.Group.MinimumVisibility;
                float fadeStep =
                    FadeOutSeconds > 0f
                        ? fadeRange * deltaTime /
                          FadeOutSeconds
                        : fadeRange;
                SetVisibility(
                    state,
                    Mathf.MoveTowards(
                        state.Visibility,
                        state.Group.MinimumVisibility,
                        fadeStep),
                    false);
                return;
            }

            if (state.Visibility >= 0.9999f)
            {
                state.ClearElapsedSeconds = 0f;
                return;
            }

            state.ClearElapsedSeconds += deltaTime;
            if (state.ClearElapsedSeconds <
                ClearHoldSeconds)
            {
                return;
            }

            float restoreRange =
                1f - state.Group.MinimumVisibility;
            float restoreStep =
                RestoreSeconds > 0f
                    ? restoreRange * deltaTime /
                      RestoreSeconds
                    : restoreRange;
            SetVisibility(
                state,
                Mathf.MoveTowards(
                    state.Visibility,
                    1f,
                    restoreStep),
                false);
        }

        private bool TryBuildPlayerSamples()
        {
            bool hasBounds = false;
            Bounds combinedBounds = default;
            for (int index = 0;
                 index < playerRenderers.Count;
                 index++)
            {
                Renderer renderer = playerRenderers[index];
                if (renderer == null ||
                    !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Bounds bounds = renderer.bounds;
                if (!hasBounds)
                {
                    combinedBounds = bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(bounds);
                }
            }

            if (!hasBounds)
            {
                return false;
            }

            HomeOcclusionResolver.BuildPlayerSamples(
                combinedBounds,
                targetCamera.transform.right,
                targetCamera.transform.up,
                playerSamples);
            return true;
        }

        private bool IsGroupOccluding(GroupState state)
        {
            Vector3 cameraPosition =
                targetCamera.transform.position;
            for (int rendererIndex = 0;
                 rendererIndex < state.Renderers.Length;
                 rendererIndex++)
            {
                Renderer renderer =
                    state.Renderers[rendererIndex].Renderer;
                if (renderer == null ||
                    !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Bounds bounds = renderer.bounds;
                for (int sampleIndex = 0;
                     sampleIndex <
                     HomeOcclusionResolver
                         .ProtectedPlayerSampleCount;
                     sampleIndex++)
                {
                    if (HomeOcclusionResolver
                        .SegmentIntersectsBounds(
                            cameraPosition,
                            playerSamples[sampleIndex],
                            bounds))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool ShouldSuspend()
        {
            if (targetCamera == null ||
                !targetCamera.isActiveAndEnabled)
            {
                return true;
            }

            if (!requireHomeGameplayState)
            {
                return false;
            }

            return home == null ||
                   fixedCamera == null ||
                   !fixedCamera.isActiveAndEnabled ||
                   !fixedCamera.IsInitialized ||
                   home.CameraFollow == null ||
                   !home.CameraFollow.FixedPoseActive ||
                   (home.Opening != null &&
                    home.Opening.isActiveAndEnabled) ||
                   (home.RefrigeratorInteraction != null &&
                    home.RefrigeratorInteraction.OwnsInteraction) ||
                   (home.AnimatedInteraction != null &&
                    home.AnimatedInteraction.IsActive);
        }

        private void SetVisibility(
            GroupState state,
            float visibility,
            bool force)
        {
            float clamped = Mathf.Clamp01(visibility);
            if (!force &&
                Mathf.Abs(state.Visibility - clamped) <
                0.0001f)
            {
                return;
            }

            state.Visibility = clamped;
            for (int index = 0;
                 index < state.Renderers.Length;
                 index++)
            {
                Renderer renderer = state.Renderers[index].Renderer;
                if (renderer == null)
                {
                    continue;
                }

                propertyBlock.Clear();
                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetFloat(
                    VisibilityId,
                    clamped);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        private void OnEnable()
        {
            if (IsInitialized)
            {
                ReevaluateImmediately();
            }
        }

        private void OnDisable()
        {
            if (IsInitialized)
            {
                ClearOcclusion();
            }
        }

        private void OnDestroy()
        {
            if (IsInitialized)
            {
                ClearOcclusion();
            }

            RestoreOriginalMaterials();
            groupStates.Clear();
            IsInitialized = false;
        }

        private void RestoreOriginalMaterials()
        {
            for (int groupIndex = 0;
                 groupIndex < groupStates.Count;
                 groupIndex++)
            {
                RendererState[] renderers =
                    groupStates[groupIndex].Renderers;
                for (int rendererIndex = 0;
                     rendererIndex < renderers.Length;
                     rendererIndex++)
                {
                    RendererState state =
                        renderers[rendererIndex];
                    if (state.Renderer != null &&
                        state.Renderer.sharedMaterial ==
                        ditherMaterial)
                    {
                        state.Renderer.sharedMaterial =
                            state.OriginalMaterial;
                    }
                }
            }
        }
    }
}
