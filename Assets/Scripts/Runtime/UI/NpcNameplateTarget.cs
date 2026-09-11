using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A silent role label belongs to an actor, independently of its action triggers or voice.</summary>
    [DisallowMultipleComponent]
    public sealed class NpcNameplateTarget : MonoBehaviour
    {
        private static readonly List<NpcNameplateTarget> targets = new List<NpcNameplateTarget>();
        private Renderer[] visualRenderers = Array.Empty<Renderer>();
        private Transform interactionRoot;

        public static IReadOnlyList<NpcNameplateTarget> ActiveTargets => targets;
        public Transform Head { get; private set; }
        public Transform ActorRoot => transform;
        public string LocalizationKey { get; private set; }
        public string StableId { get; private set; }
        public float HeadClearance { get; private set; } = NpcNameplatePolicy.AnchorClearance;
        public IReadOnlyList<Renderer> VisualRenderers => visualRenderers;
        public bool IsSpeaking => NpcSpeechBubbleView.IsPresentingAt(Head);

        public bool IsPresent
        {
            get
            {
                if (!isActiveAndEnabled || Head == null || !Head.gameObject.activeInHierarchy)
                    return false;
                foreach (Renderer visual in visualRenderers)
                    if (visual != null && visual.enabled && !visual.forceRenderingOff &&
                        visual.gameObject.activeInHierarchy) return true;
                return false;
            }
        }

        public static NpcNameplateTarget Attach(
            GameObject actor, string localizationKey, Transform head, string stableId = null)
        {
            if (actor == null) throw new ArgumentNullException(nameof(actor));
            if (head == null) throw new ArgumentNullException(nameof(head));
            if (string.IsNullOrWhiteSpace(localizationKey))
                throw new ArgumentException("A role needs a localized label.", nameof(localizationKey));
            if (!actor.TryGetComponent(out NpcNameplateTarget target))
                target = actor.AddComponent<NpcNameplateTarget>();
            target.Head = head;
            target.LocalizationKey = localizationKey;
            target.StableId = string.IsNullOrWhiteSpace(stableId) ? localizationKey : stableId;
            target.interactionRoot = actor.transform;
            target.visualRenderers = actor.GetComponentsInChildren<Renderer>(true);
            return target;
        }

        /// <summary>Some actors have a sibling action trigger under their own factory wrapper.</summary>
        public NpcNameplateTarget SetInteractionRoot(Transform root)
        {
            interactionRoot = root != null ? root : transform;
            return this;
        }

        /// <summary>Authored hats and raised hands need clearance above the same moving head.</summary>
        public NpcNameplateTarget SetHeadClearance(float meters)
        {
            if (float.IsNaN(meters) || float.IsInfinity(meters) || meters < 0f)
                throw new ArgumentOutOfRangeException(nameof(meters));
            HeadClearance = meters;
            return this;
        }

        public bool MatchesInteraction(IInteractable interaction)
        {
            return interaction is MonoBehaviour behaviour && behaviour != null &&
                interactionRoot != null && behaviour.transform.IsChildOf(interactionRoot);
        }

        private void OnEnable()
        {
            if (!targets.Contains(this)) targets.Add(this);
        }

        private void OnDisable() => targets.Remove(this);
        private void OnDestroy() => targets.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetTargets() => targets.Clear();
    }
}
