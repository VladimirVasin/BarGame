using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The pure Silent Hill head rule: what the hero notices, and how
    /// far his head may turn after it. Attention reaches further than
    /// the interaction radius — the head is a hint that something can
    /// be reached, not a confirmation that it already is — and the
    /// release limits are wider than the notice limits, so a target on
    /// the edge is held rather than flickered.
    /// </summary>
    public static class PlayerAttentionRules
    {
        public const float NoticeRadius = 3.6f;
        public const float ReleaseRadius = 4.2f;
        public const float NoticeHalfAngleDegrees = 75f;
        public const float ReleaseHalfAngleDegrees = 100f;
        public const float MaxHeadYawDegrees = 68f;

        /// <summary>
        /// The chin drops for items on the ground but barely rises:
        /// points of interest live at body height, never on the
        /// ceiling, so an upward crane always reads as a bug.
        /// </summary>
        public const float MaxHeadDownPitchDegrees = 32f;
        public const float MaxHeadUpPitchDegrees = 10f;

        /// <summary>
        /// A focus higher than this above the hero's feet is not a
        /// point of interest at all — nothing noticeable hangs over
        /// his head indoors or out.
        /// </summary>
        public const float MaxFocusHeightAboveFeet = 2.1f;

        /// <summary>People draw the eye before street furniture.</summary>
        public const float CharacterDistanceScale = 0.8f;

        public static bool IsNoticeable(
            Vector3 playerPosition,
            float bodyYawDegrees,
            Vector3 focus,
            bool isCurrent)
        {
            if (focus.y - playerPosition.y > MaxFocusHeightAboveFeet)
            {
                return false;
            }

            var planar = new Vector2(
                focus.x - playerPosition.x,
                focus.z - playerPosition.z);
            float distance = planar.magnitude;
            if (distance > (isCurrent ? ReleaseRadius : NoticeRadius))
            {
                return false;
            }

            if (distance < 0.25f)
            {
                return true;
            }

            float focusYaw = Mathf.Atan2(planar.x, planar.y) *
                             Mathf.Rad2Deg;
            float offAxis = Mathf.Abs(
                Mathf.DeltaAngle(bodyYawDegrees, focusYaw));
            return offAxis <= (isCurrent
                ? ReleaseHalfAngleDegrees
                : NoticeHalfAngleDegrees);
        }

        /// <summary>
        /// Lower order wins the head. Distance, with people weighted
        /// closer than they are.
        /// </summary>
        public static float ResolveOrder(
            Vector3 playerPosition,
            Vector3 focus,
            bool isCharacter)
        {
            float distance = new Vector2(
                focus.x - playerPosition.x,
                focus.z - playerPosition.z).magnitude;
            return isCharacter
                ? distance * CharacterDistanceScale
                : distance;
        }

        /// <summary>
        /// Where the head should point, clamped to what a neck can do.
        /// Yaw is relative to the body facing; pitch is positive up.
        /// </summary>
        public static void ResolveHeadAngles(
            Vector3 headPosition,
            float bodyYawDegrees,
            Vector3 focus,
            out float yawDegrees,
            out float pitchDegrees)
        {
            Vector3 delta = focus - headPosition;
            var planar = new Vector2(delta.x, delta.z);
            float planarDistance = Mathf.Max(0.05f, planar.magnitude);
            float focusYaw = Mathf.Atan2(planar.x, planar.y) *
                             Mathf.Rad2Deg;
            yawDegrees = Mathf.Clamp(
                Mathf.DeltaAngle(bodyYawDegrees, focusYaw),
                -MaxHeadYawDegrees,
                MaxHeadYawDegrees);
            pitchDegrees = Mathf.Clamp(
                Mathf.Atan2(delta.y, planarDistance) * Mathf.Rad2Deg,
                -MaxHeadDownPitchDegrees,
                MaxHeadUpPitchDegrees);
        }
    }

    /// <summary>
    /// Marks a colliderless character (the yard rider, staged NPCs) as
    /// something the hero's head notices. Colliderful targets — every
    /// IInteractable and the pooled pedestrians — are found by the
    /// physics scan and need no marker.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class PlayerAttentionMagnet : MonoBehaviour
    {
        private static readonly List<PlayerAttentionMagnet> active =
            new List<PlayerAttentionMagnet>();

        [SerializeField] private float focusHeight = 1.5f;

        public static IReadOnlyList<PlayerAttentionMagnet> Active =>
            active;

        public float FocusHeight
        {
            get => focusHeight;
            set => focusHeight = value;
        }

        public Vector3 FocusPosition =>
            transform.position + (Vector3.up * focusHeight);

        private void OnEnable()
        {
            active.Add(this);
        }

        private void OnDisable()
        {
            active.Remove(this);
        }
    }

    /// <summary>
    /// Picks what the hero is looking at and feeds it to the head. An
    /// interval scan gathers interactables, pedestrians and magnets;
    /// between scans the current target is tracked live, so a walking
    /// passer-by keeps the head, and hysteresis keeps the choice from
    /// flickering at the cone's edge.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAttentionController : MonoBehaviour
    {
        public const float ScanInterval = 0.18f;

        private readonly Collider[] overlapBuffer = new Collider[32];
        private PlayerInteractor interactor;
        private Player3DCharacterPresentation presentation;
        private AttentionTarget currentTarget;
        private float nextScanTime;

        public bool IsInitialized { get; private set; }
        public Vector3? CurrentFocus { get; private set; }

        public void Initialize(
            PlayerInteractor playerInteractor,
            Player3DCharacterPresentation characterPresentation)
        {
            interactor = playerInteractor != null
                ? playerInteractor
                : throw new ArgumentNullException(
                    nameof(playerInteractor));
            presentation = characterPresentation;
            IsInitialized = true;
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                return;
            }

            if (Time.unscaledTime >= nextScanTime)
            {
                nextScanTime = Time.unscaledTime + ScanInterval;
                ScanNow();
            }

            CurrentFocus = ResolveCurrentFocus();
            if (presentation != null)
            {
                presentation.SetAttentionFocus(CurrentFocus);
            }
        }

        /// <summary>
        /// One full selection pass. Public so a deterministic check can
        /// drive it without waiting on the interval.
        /// </summary>
        public void ScanNow()
        {
            if (!IsInitialized)
            {
                return;
            }

            Vector3 position = transform.position;
            float bodyYaw = transform.eulerAngles.y;

            // The current target survives while it stays inside the
            // widened release cone.
            if (TryResolveFocus(currentTarget, out Vector3 heldFocus) &&
                PlayerAttentionRules.IsNoticeable(
                    position,
                    bodyYaw,
                    heldFocus,
                    true))
            {
                CurrentFocus = heldFocus;
                return;
            }

            currentTarget = default;
            float bestOrder = float.PositiveInfinity;

            int count = Physics.OverlapSphereNonAlloc(
                position + (Vector3.up * 0.8f),
                PlayerAttentionRules.NoticeRadius,
                overlapBuffer,
                ~0,
                QueryTriggerInteraction.Collide);
            for (int index = 0; index < count; index++)
            {
                Collider candidateCollider = overlapBuffer[index];

                // Skip only the hero's own body. Everything in a scene
                // shares one composition root, so a root comparison
                // here would blind the head to the whole city.
                if (candidateCollider == null ||
                    candidateCollider.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (candidateCollider.gameObject.layer ==
                    CityPedestrianCollision.LayerIndex)
                {
                    Bounds bounds = candidateCollider.bounds;
                    Vector3 face = bounds.center +
                                   (Vector3.up * bounds.extents.y * 0.55f);
                    Consider(
                        AttentionTarget.ForCollider(candidateCollider),
                        face,
                        true,
                        position,
                        bodyYaw,
                        ref bestOrder);
                    continue;
                }

                MonoBehaviour[] behaviours = candidateCollider
                    .GetComponentsInParent<MonoBehaviour>(true);
                for (int behaviourIndex = 0;
                     behaviourIndex < behaviours.Length;
                     behaviourIndex++)
                {
                    if (!(behaviours[behaviourIndex] is
                            IInteractable interactable) ||
                        !interactable.CanInteract(interactor))
                    {
                        continue;
                    }

                    Consider(
                        AttentionTarget.ForInteractable(interactable),
                        interactable.InteractionPosition,
                        false,
                        position,
                        bodyYaw,
                        ref bestOrder);
                }
            }

            IReadOnlyList<PlayerAttentionMagnet> magnets =
                PlayerAttentionMagnet.Active;
            for (int index = 0; index < magnets.Count; index++)
            {
                PlayerAttentionMagnet magnet = magnets[index];
                if (magnet == null)
                {
                    continue;
                }

                Consider(
                    AttentionTarget.ForMagnet(magnet),
                    magnet.FocusPosition,
                    true,
                    position,
                    bodyYaw,
                    ref bestOrder);
            }

            CurrentFocus = ResolveCurrentFocus();
        }

        private void Consider(
            AttentionTarget target,
            Vector3 focus,
            bool isCharacter,
            Vector3 position,
            float bodyYaw,
            ref float bestOrder)
        {
            if (!PlayerAttentionRules.IsNoticeable(
                    position,
                    bodyYaw,
                    focus,
                    false))
            {
                return;
            }

            float order = PlayerAttentionRules.ResolveOrder(
                position,
                focus,
                isCharacter);
            if (order >= bestOrder)
            {
                return;
            }

            bestOrder = order;
            currentTarget = target;
        }

        private Vector3? ResolveCurrentFocus()
        {
            if (!TryResolveFocus(currentTarget, out Vector3 focus))
            {
                currentTarget = default;
                return null;
            }

            if (!PlayerAttentionRules.IsNoticeable(
                    transform.position,
                    transform.eulerAngles.y,
                    focus,
                    true))
            {
                currentTarget = default;
                return null;
            }

            return focus;
        }

        private bool TryResolveFocus(
            AttentionTarget target,
            out Vector3 focus)
        {
            switch (target.Kind)
            {
                case AttentionTargetKind.Interactable:
                    if (target.Interactable == null ||
                        (target.Interactable is UnityEngine.Object
                             unityObject &&
                         unityObject == null) ||
                        !target.Interactable.CanInteract(interactor))
                    {
                        break;
                    }

                    focus = target.Interactable.InteractionPosition;
                    return true;
                case AttentionTargetKind.Magnet:
                    if (target.Magnet == null ||
                        !target.Magnet.isActiveAndEnabled)
                    {
                        break;
                    }

                    focus = target.Magnet.FocusPosition;
                    return true;
                case AttentionTargetKind.Collider:
                    if (target.Collider == null ||
                        !target.Collider.gameObject.activeInHierarchy)
                    {
                        break;
                    }

                    Bounds bounds = target.Collider.bounds;
                    focus = bounds.center +
                            (Vector3.up * bounds.extents.y * 0.55f);
                    return true;
            }

            focus = default;
            return false;
        }

        private enum AttentionTargetKind
        {
            None = 0,
            Interactable = 1,
            Magnet = 2,
            Collider = 3
        }

        private readonly struct AttentionTarget
        {
            private AttentionTarget(
                AttentionTargetKind kind,
                IInteractable interactable,
                PlayerAttentionMagnet magnet,
                Collider collider)
            {
                Kind = kind;
                Interactable = interactable;
                Magnet = magnet;
                Collider = collider;
            }

            public AttentionTargetKind Kind { get; }
            public IInteractable Interactable { get; }
            public PlayerAttentionMagnet Magnet { get; }
            public Collider Collider { get; }

            public static AttentionTarget ForInteractable(
                IInteractable interactable)
            {
                return new AttentionTarget(
                    AttentionTargetKind.Interactable,
                    interactable,
                    null,
                    null);
            }

            public static AttentionTarget ForMagnet(
                PlayerAttentionMagnet magnet)
            {
                return new AttentionTarget(
                    AttentionTargetKind.Magnet,
                    null,
                    magnet,
                    null);
            }

            public static AttentionTarget ForCollider(
                Collider collider)
            {
                return new AttentionTarget(
                    AttentionTargetKind.Collider,
                    null,
                    null,
                    collider);
            }
        }
    }
}
