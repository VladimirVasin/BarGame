using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Room admission and combat pose adapter; all refrigerator presentation stays shared.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(900)]
    public sealed class WarehouseRefrigerator : MonoBehaviour, IInteractable
    {
        private CombatTestRoot owner;
        private bool initialized;
        private bool previousCombatFocus;
        private Func<bool> inspectionBrowsing;
        private Func<bool> pointerSelection;
        private readonly List<Renderer> heroWorldRenderers = new List<Renderer>(256);
        private readonly List<bool> originalWorldForcedOff = new List<bool>(256);
        private bool worldVisibilityCaptured;
        private bool worldVisibilityMaskHidden;
        public int Floor { get; private set; }
        public string SourceId { get; private set; }
        public HomeRefrigeratorPlan Plan { get; private set; }
        public HomeRefrigeratorView View { get; private set; }
        public HomeRefrigeratorInteraction Interaction { get; private set; }
        public string PromptKey => HomeRefrigeratorInteraction.OpenPromptKey;
        public Vector3 InteractionPosition => Plan != null ? transform.TransformPoint(Plan.ApproachPosition) : transform.position;

        internal void Initialize(CombatTestRoot root, int floor, string sourceId,
            HomeRefrigeratorPlan plan, HomeRefrigeratorView view)
        {
            owner = root;
            Floor = floor;
            SourceId = sourceId;
            Plan = plan;
            View = view;
            var trigger = gameObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = plan.TriggerCenter;
            trigger.size = plan.TriggerSize;
            Interaction = gameObject.AddComponent<HomeRefrigeratorInteraction>();
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (owner == null || interactor != owner.Player.Interactor || !isActiveAndEnabled) return false;
            if (!initialized)
            {
                if (!CanBegin()) return false;
                EnsureInitialized();
            }
            return initialized && Interaction.CanInteract(interactor);
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (CanInteract(interactor)) Interaction.BeginInteraction();
        }

        public bool BeginInteraction()
        {
            if (!isActiveAndEnabled || !CanBegin()) return false;
            EnsureInitialized();
            return initialized && Interaction.BeginInteraction();
        }

        private void EnsureInitialized()
        {
            if (initialized || owner == null || !owner.IsInitialized) return;
            inspectionBrowsing = () => Interaction.OwnsInteraction &&
                Interaction.Phase == HomeRefrigeratorInteractionPhase.Inspecting && !Interaction.ItemInspection.IsActive;
            pointerSelection = () => Interaction.ItemInspection.UsesPointerSelection;
            var context = new RefrigeratorInteractionContext(transform, owner.Player, owner.CameraFollow, owner.Prompt)
            {
                Admission = CanBegin,
                PreparingPlayer = PreparePlayer,
                RestoringPlayer = RestorePlayer,
                PlayCue = (cue, position) => RetroAudio.PlayAt(cue, position)
            };
            Interaction.Initialize(context, Plan, View);
            Interaction.PhaseChanged += OnPhaseChanged;
            initialized = true;
        }

        private bool CanBegin()
        {
            if (owner == null || !owner.IsInitialized || owner.Hero == null ||
                !GameInput.CanRead(GameInputContext.Gameplay) ||
                !owner.Hero.CanBeginStandingInspection) return false;
            Vector3 local = transform.InverseTransformPoint(owner.Player.GameObject.transform.position);
            if (Mathf.Abs(local.y - Plan.ApproachPosition.y) > .65f || local.z > -.28f ||
                (local - Plan.ApproachPosition).sqrMagnitude > 1.7f) return false;
            Vector3 origin = owner.Player.GameObject.transform.position + Vector3.up * 1.15f;
            Vector3 handle = View.HandlePivot.position;
            return !Physics.Linecast(origin, handle, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) ||
                hit.transform.IsChildOf(View.transform) || hit.transform.IsChildOf(owner.Player.GameObject.transform);
        }

        private void PreparePlayer()
        {
            previousCombatFocus = owner.Hero.CombatFocused;
            owner.Hero.PrepareStandingInspection();
            CaptureWorldVisibility();
            owner.Flashlight?.BeginInspectionAim(this, owner.Hero, owner.CameraFollow.Camera, View.InteriorRoot,
                Plan.CavitySize, inspectionBrowsing, pointerSelection);
        }

        private void RestorePlayer()
        {
            RestoreWorldVisibility();
            owner?.Flashlight?.EndInspectionAim(this);
            if (owner != null && owner.Hero != null) owner.Hero.RestoreStandingInspectionFocus(previousCombatFocus);
        }

        private void CaptureWorldVisibility()
        {
            RestoreWorldVisibility();
            // This traversal happens only on an explicit opening. Damage and
            // wardrobe may have added owned surfaces since the previous visit.
            owner.Player.GameObject.GetComponentsInChildren(true, heroWorldRenderers);
            for (int index = 0; index < heroWorldRenderers.Count; index++)
                originalWorldForcedOff.Add(heroWorldRenderers[index].forceRenderingOff);
            worldVisibilityCaptured = true;
            worldVisibilityMaskHidden = false;
        }

        private void ApplyWorldVisibility()
        {
            if (!worldVisibilityCaptured || owner == null) return;
            bool hidden = owner.Player.PresentationVisibility != null &&
                owner.Player.PresentationVisibility.IsHiddenBy(Interaction);
            if (hidden == worldVisibilityMaskHidden) return;
            worldVisibilityMaskHidden = hidden;
            for (int index = 0; index < heroWorldRenderers.Count; index++)
                if (heroWorldRenderers[index] != null)
                    heroWorldRenderers[index].forceRenderingOff = hidden || originalWorldForcedOff[index];
        }

        private void RestoreWorldVisibility()
        {
            if (!worldVisibilityCaptured) return;
            worldVisibilityCaptured = false;
            worldVisibilityMaskHidden = false;
            for (int index = 0; index < heroWorldRenderers.Count; index++)
                if (heroWorldRenderers[index] != null)
                    heroWorldRenderers[index].forceRenderingOff = originalWorldForcedOff[index];
            heroWorldRenderers.Clear();
            originalWorldForcedOff.Clear();
        }

        private void OnPhaseChanged(HomeRefrigeratorInteractionPhase phase) => ApplyWorldVisibility();
        // Damage/wardrobe writers may update enabled later than the shared
        // visibility lease. A forced rendering mask keeps every owned surface
        // hidden while the physical rig and its existing chest light stay live.
        private void LateUpdate() => ApplyWorldVisibility();

        private void OnDisable()
        {
            Interaction?.CancelInteraction();
            RestoreWorldVisibility();
        }

        private void OnDestroy()
        {
            if (Interaction != null) Interaction.PhaseChanged -= OnPhaseChanged;
            RestoreWorldVisibility();
        }
    }
}
