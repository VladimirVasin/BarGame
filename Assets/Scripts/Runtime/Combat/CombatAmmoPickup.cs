using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One reusable authored pack, confirmed once through the hero's shared found-item screen.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatAmmoPickup : MonoBehaviour, IInteractable
    {
        private const float WorldModelScale = 1.6f;
        private const float ShellSpacing = .045f;
        private CombatActor owner;
        private PlayerInteractor ownerInteractor;
        private IFirearmState state;
        private WorldItemFoundScreen screen;
        private SphereCollider interactionTrigger;
        private RaycastHit[] sightHits = new RaycastHit[24];
        private Func<bool> commit;
        private Func<bool> sourceAvailable;
        private Action<bool> finish;
        private Vector3 position, modelOffset, modelScale;
        private Quaternion modelRotation;
        private bool interactionEnabled = true;
        private bool available, inspecting, accepted;

        public CombatWeaponId WeaponId { get; private set; }
        public Transform Model { get; private set; }
        public bool IsAvailable => available;
        public bool IsPresenting => inspecting;
        public int RoundsPerPickup => WeaponId == CombatWeaponId.Pistol ? 8 : 4;
        public float RespawnRemaining { get; private set; }
        public string PromptKey => WeaponId == CombatWeaponId.Pistol ? "combat.ammo.pickup_pistol" : "combat.ammo.pickup_shotgun";
        public Vector3 InteractionPosition => transform.position + Vector3.up * .08f;
        internal IFirearmState State => state;

        internal void Initialize(CombatActor hero, CombatWeaponId id, IFirearmState firearm, Vector3 worldPosition)
        {
            owner = hero;
            ownerInteractor = hero.GetComponent<PlayerInteractor>();
            WeaponId = id;
            state = firearm;
            commit = Commit;
            finish = Finish;
            InventoryItemId item = id == CombatWeaponId.Pistol ? InventoryItemId.CombatPistolAmmo : InventoryItemId.CombatShotgunAmmo;
            Model = InventoryItemModelFactory.BuildWorldModel(item, transform, new Vector3(.2f, .2f, .2f));
            ConfigureWorldModel();
            Bounds bounds = WorldItemInspectionPresenter.CalculateWorldBounds(Model);
            // The root is a support contact: shelf/table height is kept through inspection and respawn.
            modelOffset = transform.InverseTransformVector(new Vector3(
                transform.position.x - bounds.center.x,
                transform.position.y - bounds.min.y + .015f,
                transform.position.z - bounds.center.z));
            modelRotation = Model.localRotation;
            modelScale = Model.localScale;
            interactionTrigger = gameObject.AddComponent<SphereCollider>();
            interactionTrigger.isTrigger = true;
            interactionTrigger.center = Vector3.up * .08f;
            interactionTrigger.radius = .23f;
            ResetPickup(worldPosition);
        }

        private void ConfigureWorldModel()
        {
            // Combat models retain their authored metric size rather than using the factory's fit box.
            // Enlarge only the pickup wrapper; imported FBX roots keep their original unit factors.
            Model.localScale *= WorldModelScale;
            Model.localRotation = WeaponId == CombatWeaponId.Pistol
                ? Quaternion.Euler(90f, 16f, 0f)
                : Quaternion.identity;
            if (WeaponId == CombatWeaponId.Shotgun)
            {
                // Keep the same four authored shells and make their silhouette readable on a shelf.
                for (int index = 0; index < Model.childCount; index++)
                    Model.GetChild(index).localPosition = new Vector3(
                        (index - (Model.childCount - 1) * .5f) * ShellSpacing, 0f, 0f);
            }
            foreach (Renderer renderer in Model.GetComponentsInChildren<Renderer>(true))
            {
                Color color;
                if (renderer.name.EndsWith("__Steel", StringComparison.Ordinal))
                    color = new Color(.47f, .50f, .43f);
                else if (renderer.name.EndsWith("__WornSteel", StringComparison.Ordinal))
                    color = new Color(.65f, .67f, .58f);
                else if (renderer.name.EndsWith("__Brass", StringComparison.Ordinal))
                    color = new Color(.72f, .57f, .31f);
                else if (renderer.name.EndsWith("__Shell", StringComparison.Ordinal))
                    color = new Color(.54f, .25f, .16f);
                else continue;
                // Per-renderer base colour leaves shared firearm materials and lighting unchanged.
                RuntimePrimitiveFactory.SetColor(renderer, color.linear);
            }
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (!isActiveAndEnabled || !interactionEnabled || !available || inspecting || owner == null ||
                !owner.isActiveAndEnabled || owner.State.IsDefeated || interactor == null || interactor != ownerInteractor ||
                !interactor.InputEnabled || interactor.InteractKeyClaimed || !GameInput.CanRead(GameInputContext.Contextual) ||
                CounterMenuInput.IsBlockedByOtherUi() || SceneTransitionService.IsTransitioning ||
                (sourceAvailable != null && !sourceAvailable())) return false;
            Vector3 from = interactor.transform.position + Vector3.up * .8f;
            Vector3 ray = InteractionPosition - from;
            if (ray.sqrMagnitude > PlayerInteractor.InteractionRadius * PlayerInteractor.InteractionRadius) return false;
            int count = Physics.RaycastNonAlloc(from, ray.normalized, sightHits, ray.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            // Retry a saturated scratch buffer rather than silently accepting an unseen obstacle.
            while (count == sightHits.Length && sightHits.Length < 96)
            {
                Array.Resize(ref sightHits, sightHits.Length * 2);
                count = Physics.RaycastNonAlloc(from, ray.normalized, sightHits, ray.magnitude,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            }
            RaycastHit[] hits = count == sightHits.Length
                ? Physics.RaycastAll(from, ray.normalized, ray.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                : sightHits;
            if (hits != sightHits) count = hits.Length;
            for (int index = 0; index < count; index++)
            {
                Transform hit = hits[index].transform;
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
            inspecting = true;
            accepted = false;
            interactionTrigger.enabled = false;
            InventoryItemId item = WeaponId == CombatWeaponId.Pistol ? InventoryItemId.CombatPistolAmmo : InventoryItemId.CombatShotgunAmmo;
            if (screen.TryPresent(interactor, item, Model, commit, finish)) return true;
            if (inspecting) Finish(false);
            return false;
        }

        private bool Commit()
        {
            if (accepted || !available || !interactionEnabled || owner == null || !owner.isActiveAndEnabled ||
                owner.State.IsDefeated || (sourceAvailable != null && !sourceAvailable())) return false;
            int before = state.ReserveRounds;
            int granted = state.AddReserveRounds(RoundsPerPickup);
            owner.JournalAmmunitionPickup(WeaponId, before, granted);
            if (granted <= 0) return false;
            accepted = true;
            available = false;
            RespawnRemaining = CombatAmmoSpawner.RespawnSeconds;
            return true;
        }

        private void Finish(bool taken)
        {
            screen = null;
            inspecting = false;
            // A committed pack remains consumed even when disable/unload interrupts its return.
            if (accepted) available = false;
            accepted = false;
            if (Model != null) Model.gameObject.SetActive(available);
            RestoreModelPose();
            interactionTrigger.enabled = interactionEnabled && available && isActiveAndEnabled;
        }

        internal void Advance(float seconds)
        {
            if (available || inspecting || !interactionEnabled || !isActiveAndEnabled) return;
            RespawnRemaining = Mathf.Max(0f, RespawnRemaining - seconds);
            if (RespawnRemaining > 0f) return;
            available = true;
            RestoreModelPose();
            Model.gameObject.SetActive(true);
            interactionTrigger.enabled = true;
        }

        internal void ResetPickup() => ResetPickup(position);

        internal void ResetPickup(Vector3 worldPosition)
        {
            CancelPickup();
            position = worldPosition;
            transform.position = position;
            RespawnRemaining = 0f;
            available = true;
            accepted = false;
            RestoreModelPose();
            Model.gameObject.SetActive(true);
            interactionTrigger.enabled = interactionEnabled && isActiveAndEnabled;
        }

        internal void SetInteractionEnabled(bool enabled)
        {
            interactionEnabled = enabled;
            if (!enabled) CancelPickup();
            if (interactionTrigger != null) interactionTrigger.enabled = enabled && available && !inspecting && isActiveAndEnabled;
        }

        internal void SetSourceAvailability(Func<bool> availability) => sourceAvailable = availability;

        internal void CancelPickup()
        {
            if (!inspecting) return;
            screen?.Abandon();
            if (inspecting) Finish(false);
        }

        private void RestoreModelPose()
        {
            if (Model == null) return;
            Model.SetParent(transform, false);
            Model.localPosition = modelOffset;
            Model.localRotation = modelRotation;
            Model.localScale = modelScale;
        }

        private void OnEnable()
        {
            if (interactionTrigger != null) interactionTrigger.enabled = interactionEnabled && available && !inspecting;
        }

        private void OnDisable() => CancelPickup();
        private void OnDestroy() => CancelPickup();
    }
}
