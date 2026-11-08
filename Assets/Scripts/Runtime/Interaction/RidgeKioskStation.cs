using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The kiosk's one world action; its four focus points belong to the menu.</summary>
    [DisallowMultipleComponent]
    public sealed class RidgeKioskStation : MonoBehaviour, IInteractable
    {
        public RidgeKioskMenuController Menu { get; private set; }
        public RidgeKioskVendor Vendor { get; private set; }
        public IReadOnlyList<RidgeKioskPurchase> Offers { get; private set; }
        public string PromptKey => "kiosk.menu.prompt";
        public Vector3 InteractionPosition => focus.position;
        private Transform focus;
        private RidgeKioskTarget reach;

        internal void Initialize(RidgeKioskVendor vendor, IReadOnlyList<RidgeKioskPurchase> offers, Transform window, Transform approach)
        {
            if (!vendor || offers == null || offers.Count != 3) throw new ArgumentException("A kiosk menu requires its existing seller and three offers.");
            Vendor = vendor; Offers = offers; focus = window;
            var trigger = new GameObject("Kiosk window interaction").transform;
            trigger.SetParent(transform, false);
            trigger.position = window.position;
            var volume = trigger.gameObject.AddComponent<SphereCollider>();
            volume.isTrigger = true; volume.radius = .48f;
            reach = RidgeKioskTarget.Attach(transform, trigger, window, approach);
            Menu = gameObject.AddComponent<RidgeKioskMenuController>();
            Menu.Initialize(this);
        }

        public bool CanReach(PlayerInteractor player) => reach && reach.CanReach(player);
        public bool CanInteract(PlayerInteractor player) => isActiveAndEnabled && Menu && !Menu.IsOpen &&
            player && player.InputEnabled && !player.InteractKeyClaimed && !CounterMenuInput.IsBlockedByOtherUi() &&
            reach && reach.IsAvailable(player);
        public void Interact(PlayerInteractor player) { if (CanInteract(player)) Menu.Open(this, player); }
        private void OnDisable() { if (Menu) Menu.Close(); }
    }
}
