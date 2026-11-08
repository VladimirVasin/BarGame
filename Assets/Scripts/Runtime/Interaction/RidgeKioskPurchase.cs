using UnityEngine;

namespace BarPromenade
{
    /// <summary>A fixed replenishable sample. Only its kiosk menu owns a purchase.</summary>
    [DisallowMultipleComponent]
    public sealed class RidgeKioskPurchase : MonoBehaviour
    {
        public InventoryItemId ItemId { get; private set; }
        public string SourceId { get; private set; }
        public Vector3 InteractionPosition => focus.position;
        public Transform OriginalRoot { get; private set; }
        private Transform focus;
        private Renderer[] renderers;

        internal void Initialize(InventoryItemId item, string key, Transform centre, Transform display, string sourcePrefix)
        {
            ItemId = item; SourceId = sourcePrefix + "." + key;
            focus = centre; OriginalRoot = display;
            renderers = display.GetComponentsInChildren<Renderer>(true);
        }

        public bool TryGetWorldBounds(out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (var renderer in renderers)
            {
                if (!renderer) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return found;
        }

    }
}
