using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The point an NPC's head turns toward when it turns toward the
    /// hero: his animated head bone, so a fallen or leaning hero is
    /// looked at where he is, or a fixed face height over the player
    /// root when no rig is mounted under it (a test stub). The rig never
    /// changes under its root, so it is resolved once.
    /// </summary>
    public sealed class HeroAttentionFocus
    {
        /// <summary>The face of a standing 1.75 m man over his feet.</summary>
        public const float FallbackHeight = 1.58f;

        private readonly Transform playerRoot;
        private Player3DAssetRegistry registry;
        private bool registryCached;

        public HeroAttentionFocus(Transform heroRoot)
        {
            playerRoot = heroRoot != null
                ? heroRoot
                : throw new ArgumentNullException(nameof(heroRoot));
        }

        public Transform Root => playerRoot;

        public Vector3 Resolve()
        {
            if (!registryCached)
            {
                registry = playerRoot
                    .GetComponentInChildren<Player3DAssetRegistry>(true);
                registryCached = registry != null;
            }

            Transform head = registry != null ? registry.Anchors.Head : null;
            return head != null
                ? head.position
                : playerRoot.position + (Vector3.up * FallbackHeight);
        }
    }

    /// <summary>
    /// The hero's notice rule seen from an NPC: a candidate is taken
    /// inside the notice cone and kept inside the wider release cone,
    /// measured from the NPC's feet and body facing exactly as the hero
    /// measures a passer-by from his, so a hero on the edge of the cone
    /// is held rather than flickered.
    /// </summary>
    public sealed class NpcAttentionNotice
    {
        public bool IsHeld { get; private set; }

        public Vector3? Resolve(
            Vector3 feetPosition,
            float bodyYawDegrees,
            Vector3? candidate)
        {
            if (!candidate.HasValue)
            {
                IsHeld = false;
                return null;
            }

            IsHeld = PlayerAttentionRules.IsNoticeable(
                feetPosition,
                bodyYawDegrees,
                candidate.Value,
                IsHeld);
            return IsHeld ? candidate : null;
        }

        public void Reset()
        {
            IsHeld = false;
        }
    }

    /// <summary>
    /// The Silent Hill head, mirrored onto an NPC: the same additive
    /// post-animation neck-and-head turn the hero makes toward a
    /// passer-by, eased in when the focus appears and eased back out
    /// when it is gone. It borrows the hero's timing, neck share and bone
    /// signs outright, so the two glances read as one gesture seen from
    /// either side; every humanoid rig in the project is built on the
    /// hero's `neck`/`head` bone specs, which is what lets the signs
    /// carry over.
    ///
    /// Call <see cref="Restore"/> before the owner's graph evaluates and
    /// <see cref="Apply"/> right after it has written the bones, so a
    /// clip that does not animate the neck can never accumulate the
    /// glance frame over frame.
    /// </summary>
    public sealed class NpcAttentionHeadLayer
    {
        private Transform facing;
        private Transform headBone;
        private Transform neckBone;
        private Vector3? focus;
        private float yaw;
        private float pitch;
        private float yawVelocity;
        private float pitchVelocity;
        private float weight;
        private Quaternion headBase = Quaternion.identity;
        private Quaternion neckBase = Quaternion.identity;
        private Quaternion headWritten = Quaternion.identity;
        private Quaternion neckWritten = Quaternion.identity;
        private bool baseCaptured;

        /// <summary>Where the head is asked to look, or <c>null</c>.</summary>
        public Vector3? Focus => focus;

        /// <summary>How far the glance is blended in, 0..1.</summary>
        public float Weight => weight;

        /// <summary>Whether a head bone is bound for the glance to turn.</summary>
        public bool HasHead => headBone != null;

        /// <summary>
        /// Binds the body facing the yaw is measured against and the two
        /// bones the turn is shared between. A missing neck turns the
        /// head alone; a missing head leaves the layer inert.
        /// </summary>
        public void Bind(Transform bodyFacing, Transform head, Transform neck)
        {
            Restore();
            facing = bodyFacing;
            headBone = head;
            neckBone = head != null ? neck : null;
            Clear();
        }

        /// <summary>
        /// The head anchor and the neck found by name under the animator,
        /// the way every humanoid rig in the project names them.
        /// </summary>
        public void Bind(
            Transform bodyFacing,
            Transform head,
            Animator animator)
        {
            Bind(
                bodyFacing,
                head,
                head != null && animator != null
                    ? FindBone(animator.transform, "neck")
                    : null);
        }

        public void Unbind()
        {
            Restore();
            Clear();
            facing = null;
            headBone = null;
            neckBone = null;
        }

        public void SetFocus(Vector3? value)
        {
            focus = value;
        }

        /// <summary>Drops the glance at once: focus, blend and smoothing.</summary>
        public void Clear()
        {
            focus = null;
            yaw = 0f;
            pitch = 0f;
            yawVelocity = 0f;
            pitchVelocity = 0f;
            weight = 0f;
        }

        /// <summary>Puts the bones back to what the graph wrote.</summary>
        public void Restore()
        {
            if (!baseCaptured)
            {
                return;
            }

            if (headBone != null)
            {
                headBone.localRotation = headBase;
            }

            if (neckBone != null)
            {
                neckBone.localRotation = neckBase;
            }

            baseCaptured = false;
        }

        /// <summary>
        /// The same, for an owner that cannot promise to call
        /// <see cref="Restore"/> before its graph runs - a late component
        /// behind a presentation it does not own. A bone still holding
        /// exactly what the layer last wrote was not touched by the
        /// animation this frame and goes back to its base; a bone the
        /// animation rewrote keeps the fresh pose. Exact equality on
        /// purpose: a clip's smallest motion must count as a rewrite, or a
        /// slow neck would freeze on a stale base.
        /// </summary>
        private void RestoreUntouched()
        {
            if (!baseCaptured)
            {
                return;
            }

            if (headBone != null &&
                headBone.localRotation.Equals(headWritten))
            {
                headBone.localRotation = headBase;
            }

            if (neckBone != null &&
                neckBone.localRotation.Equals(neckWritten))
            {
                neckBone.localRotation = neckBase;
            }

            baseCaptured = false;
        }

        public void Apply(float deltaTime)
        {
            RestoreUntouched();
            if (headBone == null || facing == null)
            {
                return;
            }

            bool allowed = focus.HasValue;
            float weightTarget = allowed ? 1f : 0f;
            if (allowed)
            {
                PlayerAttentionRules.ResolveHeadAngles(
                    headBone.position,
                    facing.eulerAngles.y,
                    focus.Value,
                    out float targetYaw,
                    out float targetPitch);
                if (weight <= 0.001f)
                {
                    // A fresh glance starts on target instead of swinging
                    // in from wherever the head last looked.
                    yaw = targetYaw;
                    pitch = targetPitch;
                    yawVelocity = 0f;
                    pitchVelocity = 0f;
                }
                else if (deltaTime > 0f)
                {
                    yaw = Mathf.SmoothDampAngle(
                        yaw,
                        targetYaw,
                        ref yawVelocity,
                        Player3DCharacterPresentation.AttentionTurnSmoothTime,
                        float.PositiveInfinity,
                        deltaTime);
                    pitch = Mathf.SmoothDamp(
                        pitch,
                        targetPitch,
                        ref pitchVelocity,
                        Player3DCharacterPresentation.AttentionTurnSmoothTime,
                        float.PositiveInfinity,
                        deltaTime);
                }
            }

            if (deltaTime > 0f)
            {
                weight = Mathf.MoveTowards(
                    weight,
                    weightTarget,
                    deltaTime / (weightTarget > weight
                        ? Player3DCharacterPresentation.AttentionBlendInSeconds
                        : Player3DCharacterPresentation.AttentionBlendOutSeconds));
            }

            if (weight <= 0.0001f)
            {
                return;
            }

            float boneYaw = yaw * weight *
                            Player3DCharacterPresentation.AttentionYawSign;
            float bonePitch = pitch * weight *
                              Player3DCharacterPresentation.AttentionPitchSign;
            headBase = headBone.localRotation;
            neckBase = neckBone != null
                ? neckBone.localRotation
                : Quaternion.identity;
            baseCaptured = true;
            if (neckBone != null)
            {
                neckBone.localRotation *= Quaternion.Euler(
                    bonePitch * Player3DCharacterPresentation.AttentionNeckShare,
                    boneYaw * Player3DCharacterPresentation.AttentionNeckShare,
                    0f);
                headBone.localRotation *= Quaternion.Euler(
                    bonePitch * Player3DCharacterPresentation.AttentionHeadShare,
                    boneYaw * Player3DCharacterPresentation.AttentionHeadShare,
                    0f);
            }
            else
            {
                headBone.localRotation *=
                    Quaternion.Euler(bonePitch, boneYaw, 0f);
            }

            headWritten = headBone.localRotation;
            neckWritten = neckBone != null
                ? neckBone.localRotation
                : Quaternion.identity;
        }

        public static Transform FindBone(Transform root, string boneName)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == boneName)
            {
                return root;
            }

            for (int index = 0; index < root.childCount; index++)
            {
                Transform found = FindBone(root.GetChild(index), boneName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// The hero glance for a staged NPC whose own presentation evaluates
    /// its graph in <c>LateUpdate</c> at the default order - the bartender,
    /// the cafe attendant. Runs behind it, the way the cafe pair's
    /// conversation look does, and turns the head after the hero under his
    /// own notice rule. An optional gate stands the head down while the
    /// body plays an authored beat, exactly as the hero's attention stands
    /// down for his modal clips.
    /// </summary>
    [DefaultExecutionOrder(350)]
    [DisallowMultipleComponent]
    public sealed class NpcHeroAttentionLook : MonoBehaviour
    {
        private readonly NpcAttentionHeadLayer attention =
            new NpcAttentionHeadLayer();
        private readonly NpcAttentionNotice notice =
            new NpcAttentionNotice();
        private HeroAttentionFocus hero;
        private Func<bool> headFree;

        public bool IsInitialized { get; private set; }

        /// <summary>Whether the hero holds this head this frame.</summary>
        public bool IsAttending => notice.IsHeld;

        /// <summary>Where the head is asked to look, or <c>null</c>.</summary>
        public Vector3? AttentionFocus => attention.Focus;

        /// <summary>How far the glance is blended in, 0..1.</summary>
        public float AttentionWeight => attention.Weight;

        /// <summary>
        /// <paramref name="bodyFacing"/> is the transform whose yaw the
        /// glance is measured against; <paramref name="isHeadFree"/>, when
        /// given, says whether the head may turn this frame.
        /// </summary>
        public void Initialize(
            Transform bodyFacing,
            Transform head,
            Transform neck,
            Transform heroRoot,
            Func<bool> isHeadFree = null)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The hero attention look is already initialized.");
            }

            if (bodyFacing == null)
            {
                throw new ArgumentNullException(nameof(bodyFacing));
            }

            if (head == null)
            {
                throw new ArgumentNullException(nameof(head));
            }

            hero = heroRoot != null
                ? new HeroAttentionFocus(heroRoot)
                : throw new ArgumentNullException(nameof(heroRoot));
            headFree = isHeadFree;
            attention.Bind(bodyFacing, head, neck);
            IsInitialized = true;
        }

        private void LateUpdate()
        {
            Advance(Time.deltaTime);
        }

        /// <summary>
        /// One frame, after the owner's graph has written the bones.
        /// Public so a deterministic check can step it without the
        /// player loop.
        /// </summary>
        public void Advance(float deltaTime)
        {
            if (!IsInitialized)
            {
                return;
            }

            float step = float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)
                ? 0f
                : Mathf.Max(0f, deltaTime);
            bool free = headFree == null || headFree();
            attention.SetFocus(
                notice.Resolve(
                    transform.position,
                    transform.eulerAngles.y,
                    free && hero != null ? hero.Resolve() : (Vector3?)null));
            attention.Apply(step);
        }

        private void OnDisable()
        {
            attention.Restore();
            attention.Clear();
            notice.Reset();
        }

        private void OnDestroy()
        {
            attention.Unbind();
        }
    }
}
