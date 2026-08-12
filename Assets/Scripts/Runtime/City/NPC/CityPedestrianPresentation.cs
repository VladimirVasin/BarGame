using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>
    /// Lightweight, manually advanced Idle/Walk presentation for a pooled
    /// city pedestrian. Route motion remains entirely code-owned.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityPedestrianPresentation : MonoBehaviour
    {
        public const float LocomotionBlendDuration = 0.15f;

        private PlayableGraph graph;
        private AnimationMixerPlayable locomotionMixer;
        private AnimationClipPlayable idlePlayable;
        private AnimationClipPlayable walkPlayable;
        private CityPedestrianAssetRegistry registry;
        private Vector3 modelBaseLocalPosition;
        private float animationSpeed = 0.91f;
        private float targetWalkWeight;
        private float groundedFootHeightOffset;
        private bool groundedFootHeightOffsetCaptured;
        private FootGroundingProbe footGroundingProbe;

        public bool IsInitialized { get; private set; }
        public bool IsMoving { get; private set; }
        public float WalkWeight { get; private set; }
        public float AnimationSpeed => animationSpeed;
        public CityPedestrianAssetRegistry Registry => registry;

        public void Initialize(
            CityPedestrianAssetRegistry assetRegistry)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The city pedestrian presentation is already initialized.");
            }

            registry = assetRegistry != null
                ? assetRegistry
                : throw new ArgumentNullException(nameof(assetRegistry));
            if (registry.Animator == null ||
                registry.IdleClip == null ||
                registry.WalkClip == null)
            {
                throw new InvalidOperationException(
                    "The city pedestrian registry requires an Animator and " +
                    "archetype Idle/Walk clips.");
            }

            Animator animator = registry.Animator;
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = null;
            animator.cullingMode =
                AnimatorCullingMode.CullUpdateTransforms;
            modelBaseLocalPosition = registry.ModelRoot != null
                ? registry.ModelRoot.localPosition
                : Vector3.zero;
            footGroundingProbe = FootGroundingProbe.Create(registry);

            BuildGraph(animator);
            IsInitialized = true;
            ConfigureCycle(animationSpeed, 0f);
            SetMoving(false);
        }

        public void ConfigureCycle(
            float walkAnimationSpeed,
            float phase01)
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException(
                    "Initialize the city pedestrian presentation first.");
            }

            animationSpeed = Mathf.Clamp(
                IsFinite(walkAnimationSpeed)
                    ? walkAnimationSpeed
                    : GetMinimumAnimationSpeed(),
                GetMinimumAnimationSpeed(),
                GetMaximumAnimationSpeed());
            float phase = IsFinite(phase01)
                ? Mathf.Repeat(phase01, 1f)
                : 0f;
            idlePlayable.SetSpeed(1d);
            walkPlayable.SetSpeed(animationSpeed);
            idlePlayable.SetTime(phase * registry.IdleClip.length);
            walkPlayable.SetTime(phase * registry.WalkClip.length);
            EvaluateGraph(0f);
        }

        public void SetMoving(bool moving)
        {
            SetMoving(moving, true);
        }

        public void SetMoving(bool moving, bool immediately)
        {
            if (!IsInitialized || !locomotionMixer.IsValid())
            {
                return;
            }

            IsMoving = moving;
            targetWalkWeight = moving ? 1f : 0f;
            if (immediately)
            {
                WalkWeight = targetWalkWeight;
            }

            ApplyMixerWeights();
        }

        private void ApplyMixerWeights()
        {
            locomotionMixer.SetInputWeight(0, 1f - WalkWeight);
            locomotionMixer.SetInputWeight(1, WalkWeight);
        }

        public void Advance(float deltaTime, bool moving)
        {
            Advance(deltaTime, moving, false);
        }

        public void Advance(
            float deltaTime,
            bool moving,
            bool immediately)
        {
            if (!IsInitialized)
            {
                return;
            }

            float safeDeltaTime = SanitizeDeltaTime(deltaTime);
            SetMoving(moving, immediately);
            if (!immediately &&
                Mathf.Abs(WalkWeight - targetWalkWeight) > 0.0001f)
            {
                WalkWeight = Mathf.MoveTowards(
                    WalkWeight,
                    targetWalkWeight,
                    safeDeltaTime / LocomotionBlendDuration);
                ApplyMixerWeights();
            }

            EvaluateGraph(safeDeltaTime);
        }

        public void Shutdown()
        {
            if (!IsInitialized && !graph.IsValid())
            {
                return;
            }

            RestoreModelBasePosition();
            if (graph.IsValid())
            {
                graph.Destroy();
            }

            IsInitialized = false;
            IsMoving = false;
            WalkWeight = 0f;
            targetWalkWeight = 0f;
            footGroundingProbe = null;
            registry = null;
        }

        private void BuildGraph(Animator animator)
        {
            graph = PlayableGraph.Create(
                $"City Pedestrian {GetEntityId()}");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            locomotionMixer = AnimationMixerPlayable.Create(graph, 2);
            idlePlayable = AnimationClipPlayable.Create(
                graph,
                registry.IdleClip);
            walkPlayable = AnimationClipPlayable.Create(
                graph,
                registry.WalkClip);
            idlePlayable.SetApplyFootIK(false);
            idlePlayable.SetApplyPlayableIK(false);
            walkPlayable.SetApplyFootIK(false);
            walkPlayable.SetApplyPlayableIK(false);
            graph.Connect(idlePlayable, 0, locomotionMixer, 0);
            graph.Connect(walkPlayable, 0, locomotionMixer, 1);
            locomotionMixer.SetInputWeight(0, 1f);
            locomotionMixer.SetInputWeight(1, 0f);

            AnimationPlayableOutput output =
                AnimationPlayableOutput.Create(
                    graph,
                    "City Pedestrian Animator",
                    animator);
            output.SetSourcePlayable(locomotionMixer);
            graph.Play();
        }

        private void EvaluateGraph(float deltaTime)
        {
            if (!graph.IsValid())
            {
                return;
            }

            RestoreModelBasePosition();
            graph.Evaluate(deltaTime);
            GroundFeetToPresentationRoot();
        }

        private void GroundFeetToPresentationRoot()
        {
            if (registry == null || registry.ModelRoot == null)
            {
                return;
            }

            if (!TryGetGroundingHeight(out float lowestFoot))
            {
                return;
            }

            if (!groundedFootHeightOffsetCaptured)
            {
                groundedFootHeightOffset =
                    lowestFoot - transform.position.y;
                groundedFootHeightOffsetCaptured = true;
            }

            float targetFootHeight = transform.position.y +
                                     groundedFootHeightOffset;
            registry.ModelRoot.position += Vector3.up *
                (targetFootHeight - lowestFoot);
        }

        private void RestoreModelBasePosition()
        {
            if (registry != null && registry.ModelRoot != null)
            {
                registry.ModelRoot.localPosition = modelBaseLocalPosition;
            }
        }

        private void OnDisable()
        {
            IsMoving = false;
            WalkWeight = 0f;
            targetWalkWeight = 0f;
            if (locomotionMixer.IsValid())
            {
                locomotionMixer.SetInputWeight(0, 1f);
                locomotionMixer.SetInputWeight(1, 0f);
            }

            RestoreModelBasePosition();
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private bool TryGetGroundingHeight(out float height)
        {
            if (footGroundingProbe != null &&
                footGroundingProbe.TryGetLowestHeight(out height))
            {
                return true;
            }

            height = float.PositiveInfinity;
            IncludeHeight(registry.LeftFootAnchor, ref height);
            IncludeHeight(registry.RightFootAnchor, ref height);
            return !float.IsPositiveInfinity(height);
        }

        private static void IncludeHeight(
            Transform anchor,
            ref float lowestHeight)
        {
            if (anchor != null && IsFinite(anchor.position.y))
            {
                lowestHeight = Mathf.Min(
                    lowestHeight,
                    anchor.position.y);
            }
        }

        private sealed class FootGroundingProbe
        {
            private readonly FootContactPoint[] contacts;

            private FootGroundingProbe(FootContactPoint[] contactPoints)
            {
                contacts = contactPoints;
            }

            public static FootGroundingProbe Create(
                CityPedestrianAssetRegistry assetRegistry)
            {
                if (assetRegistry == null)
                {
                    return null;
                }

                var points = new List<FootContactPoint>();
                IReadOnlyList<CityPedestrianRendererBinding> bindings =
                    assetRegistry.RendererBindings;
                for (int index = 0; index < bindings.Count; index++)
                {
                    CityPedestrianRendererBinding binding = bindings[index];
                    if (binding == null || binding.Renderer == null)
                    {
                        continue;
                    }

                    Transform foot = null;
                    string rendererName = binding.RendererName ??
                                          string.Empty;
                    if (ContainsOrdinal(
                            rendererName,
                            "LeftBootSole") ||
                        ContainsOrdinal(
                            rendererName,
                            "ShoeSole.L"))
                    {
                        foot = assetRegistry.LeftFootAnchor;
                    }
                    else if (ContainsOrdinal(
                                 rendererName,
                                 "RightBootSole") ||
                             ContainsOrdinal(
                                 rendererName,
                                 "ShoeSole.R"))
                    {
                        foot = assetRegistry.RightFootAnchor;
                    }

                    if (foot != null)
                    {
                        AddBoundsContacts(
                            points,
                            foot,
                            binding.Renderer.bounds);
                    }
                }

                return points.Count > 0
                    ? new FootGroundingProbe(points.ToArray())
                    : null;
            }

            private static bool ContainsOrdinal(
                string value,
                string pattern)
            {
                return value.IndexOf(
                    pattern,
                    StringComparison.Ordinal) >= 0;
            }

            public bool TryGetLowestHeight(out float height)
            {
                height = float.PositiveInfinity;
                for (int index = 0; index < contacts.Length; index++)
                {
                    if (contacts[index].TryGetWorldPosition(
                            out Vector3 position))
                    {
                        height = Mathf.Min(height, position.y);
                    }
                }

                return !float.IsPositiveInfinity(height);
            }

            private static void AddBoundsContacts(
                ICollection<FootContactPoint> points,
                Transform bone,
                Bounds bounds)
            {
                AddBoundsFace(points, bone, bounds, bounds.min.y);
                AddBoundsFace(points, bone, bounds, bounds.max.y);
            }

            private static void AddBoundsFace(
                ICollection<FootContactPoint> points,
                Transform bone,
                Bounds bounds,
                float y)
            {
                points.Add(new FootContactPoint(
                    bone,
                    new Vector3(bounds.min.x, y, bounds.min.z)));
                points.Add(new FootContactPoint(
                    bone,
                    new Vector3(bounds.min.x, y, bounds.max.z)));
                points.Add(new FootContactPoint(
                    bone,
                    new Vector3(bounds.max.x, y, bounds.min.z)));
                points.Add(new FootContactPoint(
                    bone,
                    new Vector3(bounds.max.x, y, bounds.max.z)));
            }
        }

        private readonly struct FootContactPoint
        {
            private readonly Transform bone;
            private readonly Vector3 boneLocalPosition;

            public FootContactPoint(
                Transform footBone,
                Vector3 worldPosition)
            {
                bone = footBone;
                boneLocalPosition = bone != null
                    ? bone.InverseTransformPoint(worldPosition)
                    : Vector3.zero;
            }

            public bool TryGetWorldPosition(out Vector3 position)
            {
                if (bone == null)
                {
                    position = default;
                    return false;
                }

                position = bone.TransformPoint(boneLocalPosition);
                return IsFinite(position.x) &&
                       IsFinite(position.y) &&
                       IsFinite(position.z);
            }
        }

        private static float SanitizeDeltaTime(float deltaTime)
        {
            return IsFinite(deltaTime)
                ? Mathf.Max(0f, deltaTime)
                : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private float GetMinimumAnimationSpeed()
        {
            return registry != null &&
                   CityPedestrianResources.TryGetArchetype(
                       registry.DesignId,
                       out CityPedestrianArchetype archetype)
                ? archetype.MinimumAnimationSpeed
                : CityPedestrianPlanner.MinimumAnimationSpeed;
        }

        private float GetMaximumAnimationSpeed()
        {
            return registry != null &&
                   CityPedestrianResources.TryGetArchetype(
                       registry.DesignId,
                       out CityPedestrianArchetype archetype)
                ? archetype.MaximumAnimationSpeed
                : CityPedestrianPlanner.MaximumAnimationSpeed;
        }
    }
}
