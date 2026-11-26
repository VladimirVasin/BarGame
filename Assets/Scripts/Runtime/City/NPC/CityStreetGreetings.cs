using System;
using UnityEngine;
using Random = System.Random;

namespace BarPromenade
{
    /// <summary>One silent exchange among the walkers currently near the City camera.</summary>
    internal sealed class CityStreetGreetings
    {
        private const float HeroRange = 24f;
        private const float PairRange = 7f;
        private const float ReplyStart = CityStreetArmPose.WaveDuration + .18f;
        private readonly Func<int, CityPedestrianPresentation> walkerAt;
        private readonly int[] candidates;
        private readonly Random random;
        private CityPedestrianPresentation first, second;
        private int firstSlot, secondSlot;
        private float countdown, elapsed;

        internal CityStreetGreetings(int slots, Func<int, CityPedestrianPresentation> walkerAt, int seed)
        {
            this.walkerAt = walkerAt;
            candidates = new int[slots];
            random = new Random(seed);
            countdown = NextInterval();
        }

        private float NextInterval() => 30f + 30f * (float)random.NextDouble();

        internal void Advance(float delta, Vector3 hero, Camera camera)
        {
            if (delta <= 0f) return;
            countdown -= delta;
            if (countdown <= 0f)
            {
                Cancel();
                countdown = NextInterval();
                SelectPair(hero, camera);
                return;
            }
            if (ReferenceEquals(first, null)) return;
            elapsed += delta;
            if (!Walking(first) || !Walking(second) || first.GreetingPartner != second ||
                second.GreetingPartner != first || walkerAt(firstSlot) != first || walkerAt(secondSlot) != second ||
                elapsed >= ReplyStart + CityStreetArmPose.WaveDuration ||
                (first.transform.position - hero).sqrMagnitude > HeroRange * HeroRange ||
                (second.transform.position - hero).sqrMagnitude > HeroRange * HeroRange)
            {
                Cancel();
                return;
            }
            first.SampleGreeting(elapsed);
            if (elapsed > ReplyStart) second.SampleGreeting(elapsed - ReplyStart);
        }

        private void SelectPair(Vector3 hero, Camera camera)
        {
            if (camera == null) return;
            int count = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                var body = walkerAt(i);
                if (!Walking(body) || (body.transform.position - hero).sqrMagnitude > HeroRange * HeroRange) continue;
                Vector3 head = body.Registry.HeadAnchor.position;
                Vector3 view = camera.WorldToViewportPoint(head);
                if (view.z < camera.nearClipPlane || view.z > camera.farClipPlane ||
                    view.x < 0f || view.x > 1f || view.y < 0f || view.y > 1f ||
                    Blocked(camera.transform.position, head)) continue;
                candidates[count++] = i;
            }
            // Reservoir sampling gives every eligible pair the same chance without storing pairs.
            int pairs = 0;
            for (int i = 0; i < count; i++)
            {
                var a = walkerAt(candidates[i]);
                for (int j = i + 1; j < count; j++)
                {
                    var b = walkerAt(candidates[j]);
                    Vector3 gap = b.transform.position - a.transform.position;
                    if (gap.sqrMagnitude > PairRange * PairRange || Mathf.Abs(gap.y) > 1.25f ||
                        Blocked(a.Registry.HeadAnchor.position, b.Registry.HeadAnchor.position)) continue;
                    if (random.Next(++pairs) != 0) continue;
                    firstSlot = candidates[i];
                    secondSlot = candidates[j];
                }
            }
            if (pairs == 0) return;
            if (random.Next(2) == 0) (firstSlot, secondSlot) = (secondSlot, firstSlot);
            first = walkerAt(firstSlot);
            second = walkerAt(secondSlot);
            first.BeginGreeting(second);
            second.BeginGreeting(first);
            elapsed = 0f;
        }

        private static bool Walking(CityPedestrianPresentation body) => body != null &&
            body.CanMakeStreetGesture && !body.RespondingToGust;

        private static bool Blocked(Vector3 from, Vector3 to) =>
            Physics.Linecast(from, to, CityPedestrianCollision.NonPedestrianMask, QueryTriggerInteraction.Ignore);

        internal void Cancel()
        {
            if (first != null) first.EndGreeting();
            if (second != null) second.EndGreeting();
            first = second = null;
        }
    }
}
