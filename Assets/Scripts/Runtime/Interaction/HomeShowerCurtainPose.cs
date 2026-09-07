using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Two authored, bone-only actions on the ordinary hero. The bathroom
    /// timeline owns positioning, neutral holds and curtain movement; this
    /// adapter uses the shared clip sampler and pelvis alignment throughout.
    /// Reversing each action supplies the matching gesture on the way out.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeShowerCurtainPose : MonoBehaviour
    {
        public const string ResourcePath = "Player/HomeShowerCurtainActions";
        public const string OutsideClipName = "ShowerCurtainOpenOutside";
        public const string InsideClipName = "ShowerCurtainCloseInside";
        public const float DurationSeconds = 1.5f;
        public const float ReachEnd = 0.25f;
        public const float ReleaseStart = 0.75f;
        public const float GripHeightAboveRoot = 1.38f;
        public const float LeadingEdgeLocalX = 1.115f;
        public const float LeadingEdgeLocalZ = 0.012f;

        // Independent entry and exit values are recorded in the action
        // manifest. Heights describe the floor; gravity owns the live root.
        public static readonly Vector3 OutsideDock = new Vector3(4.20f, 0f, 2.18f);
        public static readonly Vector3 InsideDock = new Vector3(4.20f, 0.18f, 2.74f);
        public static readonly Quaternion OutsideFacing = Quaternion.identity;
        public static readonly Quaternion InsideFacing = Quaternion.Euler(0f, 180f, 0f);

        private Player3DCharacterPresentation visual;
        private Player3DAssetRegistry registry;
        private Transform actor;
        private Transform curtain;
        private Transform grip;
        private Vector3 pelvisTarget;
        private bool reverse;
        private bool opening;
        private string activeClipName;

        public bool IsInitialized => visual != null && registry != null && curtain != null;
        public bool IsActive { get; private set; }
        public float NormalizedTime { get; private set; }
        public float OpeningAmount => opening ? PullProgress(NormalizedTime) : 1f - PullProgress(NormalizedTime);
        public bool IsInContact => IsActive && NormalizedTime >= ReachEnd && NormalizedTime <= ReleaseStart;
        public Transform ActiveGrip => grip;

        /// <summary>
        /// Contact is on the actual leading hem after its owner updates the
        /// mesh scale. Its height follows the grounded hero: the tray step
        /// changes where he grips the long vertical hem, never his feet.
        /// </summary>
        public Vector3 GripTarget
        {
            get
            {
                if (curtain == null || actor == null)
                {
                    return Vector3.zero;
                }

                Vector3 edge = curtain.TransformPoint(new Vector3(LeadingEdgeLocalX, 0f, LeadingEdgeLocalZ));
                edge.y = actor.position.y + GripHeightAboveRoot;
                return edge;
            }
        }

        public float ContactError => IsInContact && grip != null
            ? Vector3.Distance(grip.position, GripTarget)
            : 0f;

        public bool Initialize(HomeInteriorRoot home, Transform curtainTransform)
        {
            if (home == null)
            {
                throw new ArgumentNullException(nameof(home));
            }

            End();
            visual = home.Player.Visual as Player3DCharacterPresentation;
            registry = visual != null ? visual.Registry : null;
            actor = home.Player.GameObject != null ? home.Player.GameObject.transform : null;
            curtain = curtainTransform;
            if (registry == null || actor == null || curtain == null ||
                registry.Anchors.Pelvis == null || registry.Anchors.LeftGrip == null ||
                registry.Anchors.RightGrip == null || !TryAttachClips(registry))
            {
                visual = null;
                registry = null;
                return false;
            }

            return true;
        }

        /// <summary>Begin only after the timeline has rendered its grounded neutral settle.</summary>
        public bool Begin(bool opening, bool fromInside)
        {
            End();
            if (!IsInitialized)
            {
                return false;
            }

            this.opening = opening;
            reverse = fromInside ? opening : !opening;
            activeClipName = fromInside ? InsideClipName : OutsideClipName;
            grip = fromInside ? registry.Anchors.LeftGrip : registry.Anchors.RightGrip;
            // Preserve the exact neutral pelvis, including the imported
            // production model's small fore/aft offset and grounded height.
            pelvisTarget = registry.Anchors.Pelvis.position;
            if (!visual.TryBeginClip(activeClipName))
            {
                activeClipName = null;
                grip = null;
                return false;
            }

            IsActive = true;
            Apply(0f);
            return true;
        }

        public void Apply(float normalized)
        {
            if (!IsActive || visual == null || visual.ActiveClipName != activeClipName)
            {
                return;
            }

            NormalizedTime = Mathf.Clamp01(normalized);
            visual.SampleActiveClip(reverse ? 1f - NormalizedTime : NormalizedTime);
            visual.AlignActiveClipAnchor(pelvisTarget);
        }

        /// <summary>Idempotent on completion, failed preparation, cancel, disable and destroy.</summary>
        public void End()
        {
            if (IsActive && visual != null && visual.ActiveClipName == activeClipName)
            {
                visual.EndClip();
            }

            IsActive = false;
            activeClipName = null;
            grip = null;
            NormalizedTime = 0f;
        }

        public static float PullProgress(float normalized)
        {
            float t = Mathf.Clamp01((normalized - ReachEnd) / (ReleaseStart - ReachEnd));
            return t * t * (3f - 2f * t);
        }

        public static bool TryAttachClips(Player3DAssetRegistry target)
        {
            if (target == null)
            {
                return false;
            }

            var bindings = new List<Player3DAnimationBinding>(target.Animations);
            AnimationClip[] assets = Resources.LoadAll<AnimationClip>(ResourcePath);
            foreach (string name in new[] { OutsideClipName, InsideClipName })
            {
                if (target.TryGetAnimation(name, out Player3DAnimationBinding existing) && existing?.Clip != null)
                {
                    continue;
                }

                AnimationClip clip = assets.FirstOrDefault(candidate => candidate.name == name);
                if (clip == null || clip.isLooping || clip.events.Length != 0 ||
                    Mathf.Abs(clip.length - DurationSeconds) > 0.003f)
                {
                    return false;
                }

                bindings.Add(new Player3DAnimationBinding(name, "home_shower", clip, DurationSeconds, false));
            }

            target.Configure(target.Animator, target.ModelRoot, target.Renderers.ToArray(),
                target.MeshBindings.ToArray(), target.AnatomicalParts.ToArray(), bindings.ToArray(),
                target.Anchors, target.Metrics, target.SourceGeneratorVersion, target.SourcePose,
                target.SourceTriangleCount, target.BuildSignature, target.FaceAtlas);
            return true;
        }

        private void OnDisable() => End();
        private void OnDestroy() => End();
    }
}
