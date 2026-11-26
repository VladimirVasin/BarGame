using System;
using UnityEngine;

namespace BarPromenade
{
    internal sealed class CityStreetGusts
    {
        private const float Range = 22f, Duration = 1.85f;
        private readonly Func<int, CityPedestrianPresentation> walkerAt;
        private readonly int slots;
        private int nextSlot, activeSlot;
        private CityPedestrianPresentation walker;
        private float elapsed, cooldown;
        private bool strong;

        internal CityStreetGusts(int slots, Func<int, CityPedestrianPresentation> walkerAt, uint seed)
        {
            this.slots = slots;
            this.walkerAt = walkerAt;
            nextSlot = slots > 0 ? (int)(seed % (uint)slots) : 0;
        }

        internal void Advance(float delta, Vector3 hero, WindSample gust)
        {
            if (delta <= 0f) return;
            cooldown -= delta;
            bool crest = !strong && gust.Strength01 >= .74f;
            strong = gust.Strength01 >= (strong ? .56f : .74f);
            if (walker != null)
            {
                elapsed += delta;
                if (elapsed >= Duration || !walker.RespondingToGust || walkerAt(activeSlot) != walker || !Eligible(walker, hero))
                    Cancel();
                else
                    walker.BraceForGust(gust.HorizontalDirection, Mathf.SmoothStep(0f, 1f,
                        Mathf.Min(elapsed / .34f, (Duration - elapsed) / .44f)));
            }
            if (walker != null || !crest || cooldown > 0f) return;
            // Advance the starting slot across crests. Only the chosen pedestrian needs per-frame work.
            for (int n = 0; n < slots; n++)
            {
                int slot = (nextSlot + n) % slots;
                var candidate = walkerAt(slot);
                if (!Eligible(candidate, hero)) continue;
                if (Physics.Raycast(candidate.Registry.HeadAnchor.position, Vector3.up, 3f,
                    CityPedestrianCollision.NonPedestrianMask, QueryTriggerInteraction.Ignore)) continue;
                nextSlot = (slot + 1) % slots;
                walker = candidate;
                activeSlot = slot;
                elapsed = 0f;
                cooldown = 12f;
                walker.BraceForGust(gust.HorizontalDirection, 0f);
                return;
            }
        }

        private static bool Eligible(CityPedestrianPresentation candidate, Vector3 hero) =>
            candidate != null && candidate.CanMakeStreetGesture && candidate.GreetingPartner == null &&
            (candidate.transform.position - hero).sqrMagnitude <= Range * Range;

        internal void Cancel()
        {
            if (walker != null) walker.EndGust();
            walker = null;
        }
    }
}
