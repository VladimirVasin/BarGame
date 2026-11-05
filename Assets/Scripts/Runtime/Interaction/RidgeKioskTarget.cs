using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The four offers at one small window share the player's optical aim.</summary>
    public sealed class RidgeKioskTarget : MonoBehaviour
    {
        private Transform kiosk, focus, approach;
        private Collider trigger;
        private Window window;

        private sealed class Window
        {
            public readonly List<RidgeKioskTarget> Targets = new List<RidgeKioskTarget>(4);
            public PlayerInteractor Player;
            public int Frame = -1;
            public RidgeKioskTarget Selected;
        }

        public static RidgeKioskTarget Attach(Transform group, Transform host, Transform focus, Transform approach)
        {
            var first = group.GetComponentInChildren<RidgeKioskTarget>(true);
            var target = host.gameObject.AddComponent<RidgeKioskTarget>();
            target.kiosk = group;
            target.focus = focus;
            target.approach = approach;
            target.trigger = host.GetComponent<Collider>();
            // Construct the fixed family once. There is no scene-wide registry.
            target.window = first != null ? first.window : new Window();
            target.window.Targets.Add(target);
            target.window.Frame = -1;
            return target;
        }

        public bool CanReach(PlayerInteractor interactor)
        {
            if (!isActiveAndEnabled || interactor == null || focus == null) return false;
            Vector3 ground = interactor.transform.position;
            Vector3 offset = ground - approach.position;
            if (Mathf.Abs(offset.y) > .55f || offset.sqrMagnitude > 2.15f * 2.15f ||
                Vector3.Dot(ground - kiosk.position, kiosk.forward) < 1.34f) return false;

            Vector3 eye = ground + Vector3.up * 1.5f;
            Vector3 delta = focus.position - eye;
            // Products and the vendor's window target have trigger colliders only.
            return !Physics.Raycast(eye, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        }

        public bool IsAvailable(PlayerInteractor interactor)
        {
            if (window.Frame == Time.frameCount && window.Player == interactor)
                return window.Selected == this;
            window.Frame = Time.frameCount;
            window.Player = interactor;
            window.Selected = null;
            Camera camera = Camera.main;
            if (camera == null || interactor == null) return false;
            Transform view = camera.transform;
            Vector3 origin = interactor.transform.position + Vector3.up * .8f;
            RidgeKioskTarget best = null;
            float highest = .84f;
            foreach (var target in window.Targets)
            {
                if (target == null || !target.isActiveAndEnabled) continue;
                if (target.trigger != null && (target.trigger.ClosestPoint(origin) - origin).sqrMagnitude >
                    PlayerInteractor.InteractionRadius * PlayerInteractor.InteractionRadius) continue;
                float score = Vector3.Dot(view.forward, (target.focus.position - view.position).normalized);
                if (score <= highest || !target.CanReach(interactor)) continue;
                highest = score;
                best = target;
            }
            window.Selected = best;
            return best == this;
        }
    }
}
