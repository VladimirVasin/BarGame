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
        private PlayerInteractor hero, pendingHero;
        private Transform heroHead, npcBuyer;
        internal bool HeroHasWindow => pendingHero || Menu.IsOpen || Vendor.Session != null && Vendor.Session.IsActive;
        internal Transform Customer => npcBuyer && (npcBuyer.position - focus.position).sqrMagnitude < 2.6f
            ? npcBuyer : HeroHasWindow ? heroHead : null;
        internal bool WindowAvailable => Vendor.AtCounter && !npcBuyer;
        internal bool HasCustomerNearby => HeroHasWindow || npcBuyer || hero && reach.CanReach(hero);

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
            Vendor.BindWindow(reach);
            Menu = gameObject.AddComponent<RidgeKioskMenuController>();
            Menu.Initialize(this);
        }

        public bool CanReach(PlayerInteractor player) => reach && reach.CanReach(player);
        public bool CanInteract(PlayerInteractor player) => isActiveAndEnabled && Menu && !Menu.IsOpen &&
            player && player.InputEnabled && !player.InteractKeyClaimed && !CounterMenuInput.IsBlockedByOtherUi() &&
            reach && reach.IsAvailable(player);
        public void Interact(PlayerInteractor player)
        {
            if (!CanInteract(player)) return;
            if (WindowAvailable) Menu.Open(this, player);
            else pendingHero = player;
        }

        internal void BindHero(PlayerInteractor player, Transform head)
        {
            hero = player;
            heroHead = head;
        }

        internal bool TryClaimNpc(Transform buyer)
        {
            if (!buyer || npcBuyer || HeroHasWindow) return false;
            npcBuyer = buyer;
            return true;
        }

        internal bool OwnsNpcWindow(Transform buyer) => buyer && npcBuyer == buyer;
        internal void ReleaseNpc(Transform buyer) { if (npcBuyer == buyer) npcBuyer = null; }

        internal void ServePendingHero()
        {
            if (!pendingHero) return;
            if (!CanReach(pendingHero) || !pendingHero.InputEnabled || CounterMenuInput.IsBlockedByOtherUi())
            { pendingHero = null; return; }
            if (!WindowAvailable) return;
            PlayerInteractor player = pendingHero;
            pendingHero = null;
            Menu.Open(this, player);
        }

        private void OnDisable()
        {
            pendingHero = null;
            npcBuyer = null;
            if (Menu) Menu.Close();
        }
    }
}
