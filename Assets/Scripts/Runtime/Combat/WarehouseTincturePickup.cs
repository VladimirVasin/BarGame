using System;
using System.Globalization;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One session-persistent bottle on an authored shelf; the shared found screen owns its presentation.</summary>
    [DisallowMultipleComponent]
    public sealed class WarehouseTincturePickup : MonoBehaviour, IInteractable, IPrioritizedInteractable, IWarehouseLockerStock
    {
        private CombatTestRoot owner;
        private WorldItemFoundScreen screen;
        private SphereCollider trigger;
        private Func<bool> sourceAvailable, commit;
        private Action<bool> finish;
        private RaycastHit[] sightHits = new RaycastHit[24];
        private bool presenting, accepted;
        private WarehouseLootOutline outline;

        public string SourceId { get; private set; }
        public Transform Model { get; private set; }
        public bool IsAvailable => !GameSessionState.IsWorldItemCollected(SourceId);
        public bool IsPresenting => presenting;
        public int RenderedOutlineCount => outline?.RenderedPartCount ?? 0;
        public string PromptKey => "interaction.take_hawthorn_tincture";
        public Vector3 InteractionPosition => transform.position + Vector3.up * .05f;
        public object InteractionPriorityGroup { get; private set; }
        public int InteractionPriority => 2;

        public static string SourceIdForFloor(int floor)
        {
            if (floor < 0 || floor >= WarehouseArenaPlan.TinctureCacheCount)
                throw new ArgumentOutOfRangeException(nameof(floor));
            // A new layout cannot refill the session inventory; only BeginNewGame clears these receipts.
            return "warehouse.hawthorn.floor." + floor.ToString(CultureInfo.InvariantCulture);
        }

        internal static WarehouseTincturePickup Create(Transform parent, CombatTestRoot owner, int floor, Vector3 position)
        {
            string sourceId = SourceIdForFloor(floor);
            if (GameSessionState.IsWorldItemCollected(sourceId)) return null;
            var host = new GameObject("Hawthorn tincture");
            host.transform.SetParent(parent, false);
            host.transform.localPosition = position;
            var pickup = host.AddComponent<WarehouseTincturePickup>();
            pickup.owner = owner;
            pickup.SourceId = sourceId;
            pickup.commit = pickup.Commit;
            pickup.finish = pickup.Finish;
            pickup.Model = InventoryItemModelFactory.BuildWorldModel(InventoryItemId.HawthornTincture,
                host.transform, new Vector3(.045f, .105f, .045f));
            Bounds bounds = WorldItemInspectionPresenter.CalculateWorldBounds(pickup.Model);
            pickup.Model.position += new Vector3(host.transform.position.x - bounds.center.x,
                host.transform.position.y - bounds.min.y + .003f, host.transform.position.z - bounds.center.z);
            pickup.trigger = host.AddComponent<SphereCollider>();
            pickup.trigger.isTrigger = true;
            pickup.trigger.center = Vector3.up * .05f;
            pickup.trigger.radius = .20f;
            pickup.outline = new WarehouseLootOutline(pickup.Model);
            return pickup;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (!SourceReady() || presenting || interactor == null || interactor != owner.Player.Interactor ||
                !interactor.InputEnabled || interactor.InteractKeyClaimed ||
                !GameInput.CanRead(GameInputContext.Contextual) || CounterMenuInput.IsBlockedByOtherUi() ||
                SceneTransitionService.IsTransitioning || !GameCursor.ApplicationFocused) return false;
            Vector3 from = interactor.transform.position + Vector3.up * .8f;
            Vector3 ray = InteractionPosition - from;
            if (ray.sqrMagnitude > PlayerInteractor.InteractionRadius * PlayerInteractor.InteractionRadius) return false;
            int count = Physics.RaycastNonAlloc(from, ray.normalized, sightHits, ray.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
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
                if (!hits[index].transform.IsChildOf(owner.Hero.transform) && !hits[index].transform.IsChildOf(transform)) return false;
            return true;
        }

        private bool SourceReady() => isActiveAndEnabled && IsAvailable && owner != null &&
            owner.isActiveAndEnabled && owner.IsInitialized && owner.Hero != null &&
            !owner.Hero.State.IsDefeated && !owner.Hero.IsRestrained && !owner.RoomAttackCinematicActive &&
            (sourceAvailable == null || sourceAvailable());

        public void Interact(PlayerInteractor interactor) => TryBeginPickup(interactor);

        public bool TryBeginPickup(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor)) return false;
            screen = WorldItemFoundScreen.For(interactor);
            if (screen == null || !screen.IsInitialized) { screen = null; return false; }
            presenting = true;
            outline?.Hide();
            accepted = false;
            trigger.enabled = false;
            if (screen.TryPresent(interactor, InventoryItemId.HawthornTincture, Model, commit, finish)) return true;
            if (presenting) Finish(false);
            return false;
        }

        private bool Commit()
        {
            if (accepted || !SourceReady()) return false;
            accepted = GameSessionState.TryCollectWorldItem(SourceId, InventoryItemId.HawthornTincture);
            return accepted;
        }

        private void Finish(bool taken)
        {
            screen = null;
            presenting = false;
            // The session receipt owns the grant even if disable/unload interrupted the return animation.
            bool available = !accepted && IsAvailable;
            accepted = false;
            if (Model != null) Model.gameObject.SetActive(available);
            if (trigger != null) trigger.enabled = available && isActiveAndEnabled;
        }

        void IWarehouseLockerStock.SetSourceAvailability(Func<bool> available, object priorityGroup)
        { sourceAvailable = available; InteractionPriorityGroup = priorityGroup; }

        public void CancelPickup()
        {
            if (!presenting) return;
            screen?.Abandon();
            if (presenting) Finish(false);
        }

        private void OnEnable()
        {
            if (trigger == null) return;
            bool available = IsAvailable;
            trigger.enabled = available && !presenting;
            if (Model != null) Model.gameObject.SetActive(available);
        }

        private void LateUpdate()
        {
            outline?.Present(owner != null ? owner.CameraFollow?.Camera : null,
                owner != null && owner.Hero != null ? owner.Hero.transform : null,
                !presenting && SourceReady());
        }

        private void OnDisable() { outline?.Hide(); CancelPickup(); }
        private void OnDestroy() { CancelPickup(); outline?.Dispose(); outline = null; }
    }
}
