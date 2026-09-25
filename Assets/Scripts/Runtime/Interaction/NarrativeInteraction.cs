using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Repeatable inspection; the subject stays in the world and grants nothing.</summary>
    [DisallowMultipleComponent]
    public sealed class NarrativeInteraction : MonoBehaviour, IInteractable
    {
        private readonly RaycastHit[] sightHits = new RaycastHit[24];
        private NarrativeInteractionController session;
        private Vector3 interactionPosition;
        private float radius;
        private SphereCollider trigger;
        public NarrativeInteractionDefinition Definition { get; private set; }
        public NarrativeStagingPlan Staging { get; private set; }
        public Transform SubjectRoot { get; private set; }
        public string PromptKey => Definition?.PromptKey ?? string.Empty;
        public Vector3 InteractionPosition => interactionPosition;
        public bool IsPresenting => session != null && session.IsActive && session.Target == this;
        /// <summary>Optional follow-up after the final page is confirmed and all inspection owners release.</summary>
        public event Action<PlayerInteractor> Completed;
        public string CompletionActionKey { get; set; }

        public void Configure(NarrativeInteractionDefinition definition, Transform subjectRoot,
            NarrativeStagingPlan staging, Vector3 position, float interactionRadius = PlayerInteractor.InteractionRadius)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (subjectRoot == null) throw new ArgumentNullException(nameof(subjectRoot));
            if (float.IsNaN(interactionRadius) || float.IsInfinity(interactionRadius) || interactionRadius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(interactionRadius));
            if (IsPresenting) session.RestoreImmediate();
            Definition = definition; SubjectRoot = subjectRoot; Staging = staging;
            interactionPosition = position; radius = interactionRadius;
            if (trigger == null)
            {
                var anchor = new GameObject("Narrative interaction anchor");
                anchor.transform.SetParent(transform, false);
                trigger = anchor.AddComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.radius = .35f;
            }
            trigger.transform.position = position;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (!isActiveAndEnabled || Definition == null || SubjectRoot == null || !SubjectRoot.gameObject.activeInHierarchy ||
                interactor == null || !interactor.isActiveAndEnabled || !interactor.InputEnabled || interactor.InteractKeyClaimed ||
                IsPresenting || CounterMenuInput.IsBlockedByOtherUi() || SceneTransitionService.IsTransitioning ||
                (interactor.transform.position + Vector3.up * .8f - interactionPosition).sqrMagnitude > radius * radius)
                return false;
            // The close anchor sits on an accessible side of a large prop. A trigger
            // overlap on its far side must not permit a button through a wall/body.
            Vector3 origin = interactor.transform.position + Vector3.up * .8f;
            Vector3 delta = interactionPosition - origin;
            if (delta.magnitude < .02f) return true;
            int count = Physics.RaycastNonAlloc(origin, delta.normalized, sightHits, delta.magnitude,
                PlayerInteractor.InteractionLayerMask, QueryTriggerInteraction.Ignore);
            if (count == sightHits.Length) return false;
            for (int index = 0; index < count; index++)
            {
                Transform hit = sightHits[index].transform;
                if (hit != null && hit != interactor.transform && !hit.IsChildOf(interactor.transform)) return false;
            }
            return true;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor)) return;
            session = NarrativeInteractionController.For(interactor);
            session.Begin(this, interactor);
        }

        internal void SetSession(NarrativeInteractionController controller) => session = controller;
        internal void Complete(PlayerInteractor interactor) => Completed?.Invoke(interactor);

        private void OnDisable() { if (IsPresenting) session.RestoreImmediate(); }
        private void OnDestroy() { if (IsPresenting) session.RestoreImmediate(); }
    }
}
