using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Dynamic body data consumed by presentation-independent helpers such as
    /// contact shadows without depending on a renderer implementation.
    /// </summary>
    public readonly struct PlayerPresentationMetrics
    {
        public PlayerPresentationMetrics(
            float standingHeight,
            float bodyRadius,
            Transform facingTransform,
            Vector3 leftFootWorldPosition,
            Vector3 rightFootWorldPosition,
            float footPlantAmount,
            float fallAmount,
            float fallDirection)
        {
            StandingHeight = standingHeight;
            BodyRadius = bodyRadius;
            FacingTransform = facingTransform;
            LeftFootWorldPosition = leftFootWorldPosition;
            RightFootWorldPosition = rightFootWorldPosition;
            FootPlantAmount = Mathf.Clamp01(footPlantAmount);
            FallAmount = Mathf.Clamp01(fallAmount);
            FallDirection = Mathf.Approximately(fallDirection, 0f)
                ? 1f
                : Mathf.Sign(fallDirection);
        }

        public float StandingHeight { get; }
        public float BodyRadius { get; }
        public Transform FacingTransform { get; }
        public Vector3 LeftFootWorldPosition { get; }
        public Vector3 RightFootWorldPosition { get; }
        public float FootPlantAmount { get; }
        public float FallAmount { get; }
        public float FallDirection { get; }
    }

    public interface IPlayerMotionPresentation
    {
        void SetMotion(Vector3 planarVelocity);
    }

    public interface IPlayerStatusPresentation
    {
        void SetIntoxication(float intensity);
        void SetBalancePose(float signedLean);
        void SetFallPose(float signedDirection, float amount);
        void SetFallAnimation(
            PlayerFallAnimationPhase phase,
            float normalizedProgress);
    }

    /// <summary>
    /// Deterministic full-body clip playback used by positioned interactions.
    /// Gameplay owns normalized time; Animator transitions and Animation Events
    /// never advance interaction transactions.
    /// </summary>
    public interface IPlayerClipPresentation
    {
        string ActiveClipName { get; }
        bool IsClipActive { get; }

        bool HasClip(string clipName);
        bool TryBeginClip(string clipName);
        void SampleActiveClip(float normalizedTime);
        void AlignActiveClipAnchor(Vector3 worldPelvisTarget);
        void ResetClipSpatialOffset();
        void EndClip();
    }

    public interface IPlayerPresentation :
        IPlayerMotionPresentation,
        IPlayerStatusPresentation
    {
        IReadOnlyList<Renderer> Renderers { get; }
        Transform VisualRoot { get; }
        PlayerPresentationMetrics Metrics { get; }
        bool InteractionHandoffLocked { get; }

        void SetInteractionHandoffLocked(bool locked);
    }

    [Flags]
    public enum PlayerPresentationVisibilityScope
    {
        None = 0,
        Renderers = 1,
        Shadows = 2,
        All = Renderers | Shadows
    }

    /// <summary>
    /// Coordinates temporary presentation hiding across nested modal owners.
    /// The first lease snapshots enabled state and the final lease restores it.
    /// </summary>
    public sealed class PlayerPresentationVisibility
    {
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance =
                new ReferenceComparer();

            public new bool Equals(object left, object right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(object value)
            {
                return RuntimeHelpers.GetHashCode(value);
            }
        }

        private sealed class Lease : IDisposable
        {
            private PlayerPresentationVisibility owner;
            private readonly object leaseOwner;
            private readonly PlayerPresentationVisibilityScope scope;

            public Lease(
                PlayerPresentationVisibility visibility,
                object ownerToken,
                PlayerPresentationVisibilityScope visibilityScope)
            {
                owner = visibility;
                leaseOwner = ownerToken;
                scope = visibilityScope;
            }

            public void Dispose()
            {
                PlayerPresentationVisibility visibility = owner;
                if (visibility == null)
                {
                    return;
                }

                owner = null;
                visibility.Release(leaseOwner, scope);
            }
        }

        private readonly IPlayerPresentation presentation;
        private readonly Behaviour[] shadows;
        private readonly Dictionary<object, int> rendererOwners =
            new Dictionary<object, int>(ReferenceComparer.Instance);
        private readonly Dictionary<object, int> shadowOwners =
            new Dictionary<object, int>(ReferenceComparer.Instance);

        private Renderer[] capturedRenderers = Array.Empty<Renderer>();
        private bool[] capturedRendererStates = Array.Empty<bool>();
        private bool[] capturedShadowStates = Array.Empty<bool>();
        private int rendererLeaseCount;
        private int shadowLeaseCount;

        public PlayerPresentationVisibility(
            IPlayerPresentation playerPresentation,
            params Behaviour[] shadowBehaviours)
        {
            presentation = playerPresentation ??
                throw new ArgumentNullException(nameof(playerPresentation));
            shadows = shadowBehaviours ?? Array.Empty<Behaviour>();
        }

        public bool RenderersHidden => rendererLeaseCount > 0;
        public bool ShadowsHidden => shadowLeaseCount > 0;
        public bool IsHidden => RenderersHidden || ShadowsHidden;

        public IDisposable AcquireHidden(
            object owner,
            PlayerPresentationVisibilityScope scope =
                PlayerPresentationVisibilityScope.All)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (scope == PlayerPresentationVisibilityScope.None ||
                (scope & ~PlayerPresentationVisibilityScope.All) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(scope),
                    scope,
                    "A renderer, shadow or combined visibility scope is required.");
            }

            if ((scope & PlayerPresentationVisibilityScope.Renderers) != 0)
            {
                if (rendererLeaseCount == 0)
                {
                    CaptureAndHideRenderers();
                }

                AddOwner(rendererOwners, owner);
                rendererLeaseCount++;
            }

            if ((scope & PlayerPresentationVisibilityScope.Shadows) != 0)
            {
                if (shadowLeaseCount == 0)
                {
                    CaptureAndHideShadows();
                }

                AddOwner(shadowOwners, owner);
                shadowLeaseCount++;
            }

            return new Lease(this, owner, scope);
        }

        public bool IsHiddenBy(object owner)
        {
            return owner != null &&
                   (rendererOwners.ContainsKey(owner) ||
                    shadowOwners.ContainsKey(owner));
        }

        private void Release(
            object owner,
            PlayerPresentationVisibilityScope scope)
        {
            if ((scope & PlayerPresentationVisibilityScope.Renderers) != 0 &&
                RemoveOwner(rendererOwners, owner))
            {
                rendererLeaseCount--;
                if (rendererLeaseCount == 0)
                {
                    RestoreRenderers();
                }
            }

            if ((scope & PlayerPresentationVisibilityScope.Shadows) != 0 &&
                RemoveOwner(shadowOwners, owner))
            {
                shadowLeaseCount--;
                if (shadowLeaseCount == 0)
                {
                    RestoreShadows();
                }
            }
        }

        private void CaptureAndHideRenderers()
        {
            IReadOnlyList<Renderer> renderers = presentation.Renderers;
            int count = renderers?.Count ?? 0;
            capturedRenderers = new Renderer[count];
            capturedRendererStates = new bool[count];
            for (int index = 0; index < count; index++)
            {
                Renderer renderer = renderers[index];
                capturedRenderers[index] = renderer;
                capturedRendererStates[index] =
                    renderer != null && renderer.enabled;
                if (renderer != null)
                {
                    renderer.enabled = false;
                }
            }
        }

        private void RestoreRenderers()
        {
            for (int index = 0;
                 index < capturedRenderers.Length;
                 index++)
            {
                Renderer renderer = capturedRenderers[index];
                if (renderer != null)
                {
                    renderer.enabled = capturedRendererStates[index];
                }
            }

            capturedRenderers = Array.Empty<Renderer>();
            capturedRendererStates = Array.Empty<bool>();
        }

        private void CaptureAndHideShadows()
        {
            capturedShadowStates = new bool[shadows.Length];
            for (int index = 0; index < shadows.Length; index++)
            {
                Behaviour shadow = shadows[index];
                capturedShadowStates[index] =
                    shadow != null && shadow.enabled;
                if (shadow != null)
                {
                    shadow.enabled = false;
                }
            }
        }

        private void RestoreShadows()
        {
            for (int index = 0; index < shadows.Length; index++)
            {
                Behaviour shadow = shadows[index];
                if (shadow != null)
                {
                    shadow.enabled = capturedShadowStates[index];
                }
            }

            capturedShadowStates = Array.Empty<bool>();
        }

        private static void AddOwner(
            IDictionary<object, int> owners,
            object owner)
        {
            owners.TryGetValue(owner, out int count);
            owners[owner] = count + 1;
        }

        private static bool RemoveOwner(
            IDictionary<object, int> owners,
            object owner)
        {
            if (!owners.TryGetValue(owner, out int count))
            {
                return false;
            }

            if (count <= 1)
            {
                owners.Remove(owner);
            }
            else
            {
                owners[owner] = count - 1;
            }

            return true;
        }
    }
}
