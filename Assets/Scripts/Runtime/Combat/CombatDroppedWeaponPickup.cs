using UnityEngine;

namespace BarPromenade
{
    /// <summary>Reclaims the same round prop through the shared item screen, without an inventory grant.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatDroppedWeaponPickup : MonoBehaviour, IInteractable
    {
        private CombatActor owner;
        private WorldItemFoundScreen screen;
        private SphereCollider interactionTrigger;
        private readonly RaycastHit[] sightHits = new RaycastHit[24];
        private Vector3 velocity, angularVelocity;
        private RigidbodyInterpolation interpolation;
        private bool inspecting, accepted, cancelling, disabling;

        public string PromptKey => "combat.weapon.pickup";
        public Vector3 InteractionPosition => transform.position;
        public bool IsPresenting => inspecting;

        internal void Initialize(CombatActor actor)
        {
            owner = actor;
            if (interactionTrigger == null)
            {
                interactionTrigger = gameObject.AddComponent<SphereCollider>();
                interactionTrigger.isTrigger = true;
                interactionTrigger.radius = .2f;
            }
            interactionTrigger.enabled = true;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (owner == null || !owner.CanRecoverDroppedWeapon || inspecting || interactor == null ||
                interactor.gameObject != owner.gameObject || !interactor.InputEnabled || interactor.InteractKeyClaimed ||
                !GameInput.CanRead(GameInputContext.Contextual) || CounterMenuInput.IsBlockedByOtherUi() ||
                SceneTransitionService.IsTransitioning) return false;
            Vector3 from = interactor.transform.position + Vector3.up * .8f;
            Vector3 ray = InteractionPosition - from;
            if (ray.sqrMagnitude > PlayerInteractor.InteractionRadius * PlayerInteractor.InteractionRadius) return false;
            int count = Physics.RaycastNonAlloc(from, ray.normalized, sightHits, ray.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == sightHits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                Transform hit = sightHits[i].transform;
                if (!hit.IsChildOf(owner.transform) && !hit.IsChildOf(transform)) return false;
            }
            return true;
        }

        public void Interact(PlayerInteractor interactor) => TryBeginPickup(interactor);

        public bool TryBeginPickup(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor)) return false;
            screen = WorldItemFoundScreen.For(interactor);
            if (screen == null || !screen.IsInitialized) { screen = null; return false; }
            var body = GetComponent<Rigidbody>();
            velocity = body != null ? body.linearVelocity : Vector3.zero;
            angularVelocity = body != null ? body.angularVelocity : Vector3.zero;
            interpolation = body != null ? body.interpolation : RigidbodyInterpolation.None;
            owner.SetDroppedWeaponInspected(true);
            inspecting = true;
            accepted = false;
            if (screen.TryPresent(interactor, InventoryItemId.CombatCrowbar, transform, Commit, Finish)) return true;
            if (inspecting) Finish(false);
            return false;
        }

        private bool Commit()
        {
            if (accepted || owner == null || !owner.CanRecoverDroppedWeapon) return false;
            accepted = true;
            return true;
        }

        private void Finish(bool taken)
        {
            bool equip = taken && accepted && !cancelling;
            screen = null;
            inspecting = accepted = false;
            if (owner == null) return;
            // The shared presenter has restored the actual prop's parent and pose
            // before this callback. Only now may it be attached back to the hand.
            if (equip && owner.EquipRecoveredWeapon())
            {
                interactionTrigger.enabled = false;
                return;
            }
            if (!disabling) gameObject.SetActive(true);
            owner.SetDroppedWeaponInspected(false, velocity, angularVelocity, interpolation);
        }

        internal void CancelPickup()
        {
            if (interactionTrigger != null) interactionTrigger.enabled = false;
            if (!inspecting) return;
            cancelling = true;
            var active = screen;
            active?.Abandon();
            if (inspecting) Finish(false);
            cancelling = false;
        }

        private void OnDisable()
        {
            // Confirm deliberately hides the prop until the shared return ends.
            // An unrelated disable before confirmation must release the modal.
            if (inspecting && !accepted)
            {
                disabling = true;
                CancelPickup();
                disabling = false;
            }
        }

        private void OnDestroy() => CancelPickup();
    }
}
