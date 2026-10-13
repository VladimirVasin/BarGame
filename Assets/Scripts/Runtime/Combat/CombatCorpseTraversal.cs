using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Body-only traversal policy. Weapons and world support retain their physical contacts.</summary>
    public static class CombatCorpseTraversal
    {
        private sealed class Participant
        {
            internal readonly CombatActor Actor;
            internal readonly List<Entry> Colliders = new List<Entry>(24);
            internal Participant(CombatActor actor) { Actor = actor; }
        }

        private sealed class Entry
        {
            internal readonly Participant Owner;
            internal readonly Collider Collider;
            internal readonly bool Detached;
            internal Entry(Participant owner, Collider collider, bool detached)
            { Owner = owner; Collider = collider; Detached = detached; }
        }

        private readonly struct IgnoredPair
        {
            internal readonly Entry First, Second;
            internal readonly long Key;
            internal IgnoredPair(Entry first, Entry second, long key)
            { First = first; Second = second; Key = key; }
        }

        private static readonly Dictionary<CombatActor, Participant> participants = new Dictionary<CombatActor, Participant>();
        private static readonly Dictionary<Collider, Entry> ownership = new Dictionary<Collider, Entry>();
        private static readonly List<IgnoredPair> ignored = new List<IgnoredPair>();
        private static readonly HashSet<long> ignoredKeys = new HashSet<long>();
        private static readonly Vector3[] footprintOffsets =
        {
            Vector3.right, new Vector3(.7071068f, 0f, .7071068f), Vector3.forward,
            new Vector3(-.7071068f, 0f, .7071068f), Vector3.left,
            new Vector3(-.7071068f, 0f, -.7071068f), Vector3.back,
            new Vector3(.7071068f, 0f, -.7071068f)
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearSession()
        {
            participants.Clear(); ownership.Clear(); ignored.Clear(); ignoredKeys.Clear();
        }

        internal static void RegisterActor(CombatActor actor)
        {
            if (actor == null || !actor.isActiveAndEnabled || actor.Ragdoll?.PhysicsController == null) return;
            if (!participants.ContainsKey(actor)) participants.Add(actor, new Participant(actor));
            RegisterCollider(actor, actor.Body);
            foreach (var collider in actor.Ragdoll.PhysicsController.AnatomicalColliders)
                RegisterCollider(actor, collider.Key);
            RefreshActor(actor);
        }

        internal static void RegisterCollider(CombatActor actor, Collider collider, bool detached = false)
        {
            if (actor == null || collider == null || !actor.isActiveAndEnabled) return;
            if (!participants.TryGetValue(actor, out Participant participant))
            { participant = new Participant(actor); participants.Add(actor, participant); }
            if (ownership.ContainsKey(collider)) return;
            var entry = new Entry(participant, collider, detached);
            participant.Colliders.Add(entry); ownership.Add(collider, entry);
            Reconcile(entry);
        }

        /// <summary>PhysX forgets pair exclusions when colliders are disabled; reapply at their owner's handoff.</summary>
        internal static void RefreshActor(CombatActor actor)
        {
            PrunePairs();
            if (actor == null || !participants.TryGetValue(actor, out Participant participant)) return;
            foreach (Entry entry in participant.Colliders) Reconcile(entry);
        }

        internal static void NotifyColliderEnabled(Collider collider)
        {
            if (collider != null && ownership.TryGetValue(collider, out Entry entry)) Reconcile(entry);
        }

        internal static void ReleaseCollider(Collider collider)
        {
            if (ReferenceEquals(collider, null) || !ownership.TryGetValue(collider, out Entry entry)) return;
            ownership.Remove(collider); entry.Owner.Colliders.Remove(entry);
            for (int i = ignored.Count - 1; i >= 0; i--)
                if (ignored[i].First == entry || ignored[i].Second == entry) RemovePair(i);
        }

        internal static void UnregisterActor(CombatActor actor)
        {
            if (ReferenceEquals(actor, null) || !participants.TryGetValue(actor, out Participant participant)) return;
            foreach (Entry entry in participant.Colliders) ownership.Remove(entry.Collider);
            for (int i = ignored.Count - 1; i >= 0; i--)
                if (ignored[i].First.Owner == participant || ignored[i].Second.Owner == participant) RemovePair(i);
            participants.Remove(actor);
        }

        /// <summary>Living knockdowns remain bodies. Separated parts are remains even while their former owner survives.</summary>
        public static bool IsCorpse(Collider collider) => collider != null &&
            ownership.TryGetValue(collider, out Entry entry) && IsPassable(entry);

        private static bool IsPassable(Entry entry)
        {
            CombatActor actor = entry.Owner.Actor;
            return actor != null && (entry.Detached || actor.isActiveAndEnabled && (actor.State.IsDefeated ||
                actor.Ragdoll != null && actor.Ragdoll.IsActive && !actor.Ragdoll.IsRecoverable));
        }

        private static bool Live(Collider collider) => collider != null && collider.enabled && collider.gameObject.activeInHierarchy;

        private static bool ShouldIgnore(Entry first, Entry second) => first.Owner != second.Owner &&
            first.Owner.Actor != null && (first.Detached || first.Owner.Actor.isActiveAndEnabled) &&
            second.Owner.Actor != null && (second.Detached || second.Owner.Actor.isActiveAndEnabled) &&
            IsPassable(first) != IsPassable(second);

        private static void Reconcile(Entry entry)
        {
            if (!Live(entry.Collider)) return;
            foreach (Participant participant in participants.Values)
            {
                if (participant == entry.Owner) continue;
                foreach (Entry other in participant.Colliders)
                {
                    if (!Live(other.Collider) || !ShouldIgnore(entry, other)) continue;
                    long key = PairKey(entry.Collider, other.Collider);
                    bool managed = ignoredKeys.Contains(key);
                    // Existing self/equipment policies own their own exclusions. Only restore pairs we changed.
                    if (Physics.GetIgnoreCollision(entry.Collider, other.Collider)) continue;
                    Physics.IgnoreCollision(entry.Collider, other.Collider, true);
                    if (managed) continue;
                    ignored.Add(new IgnoredPair(entry, other, key)); ignoredKeys.Add(key);
                }
            }
        }

        private static long PairKey(Collider first, Collider second)
        {
            int a = first.GetEntityId().GetHashCode(), b = second.GetEntityId().GetHashCode();
            if (a > b) { int swap = a; a = b; b = swap; }
            return ((long)(uint)a << 32) | (uint)b;
        }

        private static void PrunePairs()
        {
            for (int i = ignored.Count - 1; i >= 0; i--)
                if (!ShouldIgnore(ignored[i].First, ignored[i].Second)) RemovePair(i);
        }

        private static void RemovePair(int index)
        {
            IgnoredPair pair = ignored[index];
            // A pooled rig keeps disabled anatomical colliders. Their native pair
            // exclusion can survive disable, so restore our pair before forgetting it.
            if (pair.First.Collider != null && pair.Second.Collider != null)
                Physics.IgnoreCollision(pair.First.Collider, pair.Second.Collider, false);
            ignoredKeys.Remove(pair.Key);
            int last = ignored.Count - 1;
            ignored[index] = ignored[last]; ignored.RemoveAt(last);
        }

        /// <summary>Allocation-free footprint samples over individual collider contours, rather than a whole-body box.</summary>
        public static bool TryGetClearance(Vector3 from, Vector3 to, float footRadius, out float worldTop) =>
            TryGetClearance(from, to, footRadius, float.PositiveInfinity, out worldTop, out _);

        /// <summary>Ignores airborne surfaces above the gait's reach and reports the first sampled footprint contact.</summary>
        public static bool TryGetClearance(Vector3 from, Vector3 to, float footRadius, float maximumWorldTop,
            out float worldTop, out float earliestFraction)
        {
            worldTop = float.NegativeInfinity;
            earliestFraction = 1f;
            if (!Finite(from) || !Finite(to) || float.IsNaN(footRadius) || float.IsInfinity(footRadius) ||
                float.IsNaN(maximumWorldTop)) return false;
            footRadius = Mathf.Clamp(footRadius, 0f, .25f);
            Vector3 delta = to - from; delta.y = 0f;
            float length = delta.magnitude;
            foreach (Participant participant in participants.Values)
            {
                foreach (Entry entry in participant.Colliders)
                {
                    Collider collider = entry.Collider;
                    if (!IsPassable(entry) || !Live(collider) || collider.isTrigger || collider is CharacterController) continue;
                    Bounds bounds = collider.bounds;
                    if (bounds.max.y < Mathf.Min(from.y, to.y) - .06f || bounds.min.y > maximumWorldTop) continue;
                    Vector3 minimum = bounds.min, maximum = bounds.max;
                    minimum.x -= footRadius; minimum.z -= footRadius;
                    maximum.x += footRadius; maximum.z += footRadius;
                    float start = 0f, end = 1f;
                    if (!Clip(from.x, delta.x, minimum.x, maximum.x, ref start, ref end) ||
                        !Clip(from.z, delta.z, minimum.z, maximum.z, ref start, ref end)) continue;
                    // Queries are local gait previews. Even a caller's long segment has a fixed per-collider budget.
                    int samples = Mathf.Clamp(Mathf.CeilToInt(length * (end - start) / .06f), 1, 6);
                    float rayY = bounds.max.y + .02f, depth = bounds.size.y + .04f;
                    for (int sample = 0; sample <= samples; sample++)
                    {
                        float fraction = Mathf.Lerp(start, end, (float)sample / samples);
                        Vector3 point = from + delta * fraction;
                        point.y = rayY;
                        Sample(collider, point, depth, maximumWorldTop, fraction, ref worldTop, ref earliestFraction);
                        for (int offset = 0; offset < 8 && footRadius > 0f; offset++)
                        {
                            Vector3 edge = point + footprintOffsets[offset] * footRadius;
                            Sample(collider, edge, depth, maximumWorldTop, fraction, ref worldTop, ref earliestFraction);
                        }
                    }
                }
            }
            return !float.IsNegativeInfinity(worldTop);
        }

        private static void Sample(Collider collider, Vector3 origin, float depth, float maximumWorldTop,
            float fraction, ref float worldTop, ref float earliestFraction)
        {
            if (!collider.Raycast(new Ray(origin, Vector3.down), out RaycastHit hit, depth) || hit.point.y > maximumWorldTop) return;
            worldTop = Mathf.Max(worldTop, hit.point.y);
            earliestFraction = Mathf.Min(earliestFraction, fraction);
        }

        private static bool Clip(float origin, float delta, float minimum, float maximum, ref float start, ref float end)
        {
            if (Mathf.Abs(delta) < .000001f) return origin >= minimum && origin <= maximum;
            float near = (minimum - origin) / delta, far = (maximum - origin) / delta;
            if (near > far) { float swap = near; near = far; far = swap; }
            start = Mathf.Max(start, near); end = Mathf.Min(end, far);
            return start <= end;
        }

        private static bool Finite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
            !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
    }
}
