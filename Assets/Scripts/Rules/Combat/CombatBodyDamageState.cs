using System;
using System.Collections.Generic;

namespace BarPromenade
{
    public enum BodyDamageRegion
    {
        Head = 0, Neck = 1, Chest = 2, Abdomen = 3, Pelvis = 4,
        LeftUpperArm = 5, LeftForearm = 6, LeftHand = 7,
        RightUpperArm = 8, RightForearm = 9, RightHand = 10,
        LeftThigh = 11, LeftShin = 12, LeftFoot = 13,
        RightThigh = 14, RightShin = 15, RightFoot = 16
    }

    /// <summary>Monotonic, local tissue and structural continuity. It never spends HP.
    /// The first unit removes tissue; the next quarter breaks the underlying bone.
    /// Contacts are unique across regions and contact batches, not just within a frame.</summary>
    public sealed class CombatBodyDamageState
    {
        public const int PatchCount = 4;
        public const int RegionCount = 17;
        public const float SeparationTrauma = 1.25f;
        private const int ShotHistory = 64;
        private readonly float[] trauma = new float[RegionCount * PatchCount];
        private readonly float[] limbHealthSpent = new float[4];
        private readonly bool[] attached = new bool[RegionCount];
        private readonly Dictionary<ShotKey, uint> shots = new Dictionary<ShotKey, uint>();
        private readonly Queue<ShotKey> shotOrder = new Queue<ShotKey>();
        private readonly Dictionary<int, int> newestShot = new Dictionary<int, int>();
        private readonly Dictionary<int, int> retiredShot = new Dictionary<int, int>();
        private static readonly int[] Parents = { 1, 2, 3, 4, -1, 2, 5, 6, 2, 8, 9, 4, 11, 12, 4, 14, 15 };

        public CombatBodyDamageState() => Reset();
        public bool IsTerminal => !IsAttached(BodyDamageRegion.Head) || !IsAttached(BodyDamageRegion.Neck) ||
            !IsAttached(BodyDamageRegion.Chest) || !IsAttached(BodyDamageRegion.Abdomen) || !IsAttached(BodyDamageRegion.Pelvis);
        public bool CanUseRightHand => !IsTerminal && IsFunctional(BodyDamageRegion.RightUpperArm) &&
            IsFunctional(BodyDamageRegion.RightForearm) && IsFunctional(BodyDamageRegion.RightHand);
        public bool CanUseLeftHand => !IsTerminal && IsFunctional(BodyDamageRegion.LeftUpperArm) &&
            IsFunctional(BodyDamageRegion.LeftForearm) && IsFunctional(BodyDamageRegion.LeftHand);
        public bool CanStand => !IsTerminal && LegFunctional(true) && LegFunctional(false);
        public bool CanRise => CanStand && (CanUseLeftHand || CanUseRightHand);
        public bool CanCrawl => !IsTerminal && (CanUseLeftHand || CanUseRightHand);

        public float TissueLoss(BodyDamageRegion region, int patch) => Math.Min(1f, trauma[Index(region, patch)]);
        public float BoneContinuity(BodyDamageRegion region, int patch) =>
            Math.Max(0f, 1f - Math.Max(0f, trauma[Index(region, patch)] - 1f) / (SeparationTrauma - 1f));
        public float BoneContinuity(BodyDamageRegion region)
        {
            float continuity = 1f;
            for (int patch = 0; patch < PatchCount; patch++) continuity = Math.Min(continuity, BoneContinuity(region, patch));
            return continuity;
        }
        public bool IsAttached(BodyDamageRegion region) => attached[RegionIndex(region)];
        public bool IsFunctional(BodyDamageRegion region)
        {
            if (!IsAttached(region) || BoneContinuity(region) <= .25f) return false;
            // A continuous bone is not an intact muscle/tendon chain. A near-total
            // local tissue loss removes this segment's support and grip capability.
            for (int patch = 0; patch < PatchCount; patch++) if (TissueLoss(region, patch) >= .9f) return false;
            return true;
        }

        /// <summary>Deliberate gameplay model: an entire arm can cost at most 35 HP,
        /// a leg at most 45 HP. Regional structural loss remains independent, allowing
        /// actual projectile contacts to remove limbs without always killing first.
        /// The caller supplies zero for an already defeated actor.</summary>
        public float ResolveHealthDamage(BodyDamageRegion region, float requested)
        {
            RegionIndex(region);
            if (float.IsNaN(requested) || float.IsInfinity(requested) || requested < 0f)
                throw new ArgumentOutOfRangeException(nameof(requested));
            int group = region switch
            {
                BodyDamageRegion.LeftUpperArm or BodyDamageRegion.LeftForearm or BodyDamageRegion.LeftHand => 0,
                BodyDamageRegion.RightUpperArm or BodyDamageRegion.RightForearm or BodyDamageRegion.RightHand => 1,
                BodyDamageRegion.LeftThigh or BodyDamageRegion.LeftShin or BodyDamageRegion.LeftFoot => 2,
                BodyDamageRegion.RightThigh or BodyDamageRegion.RightShin or BodyDamageRegion.RightFoot => 3,
                _ => -1
            };
            if (group < 0) return requested;
            float resolved = Math.Min(requested, Math.Max(0f, (group < 2 ? 35f : 45f) - limbHealthSpent[group]));
            limbHealthSpent[group] += resolved;
            return resolved;
        }

        public bool Apply(BodyDamageRegion region, int patch, float amount, int source, int sequence, int pellet)
        {
            int index = Index(region, patch);
            if (float.IsNaN(amount) || float.IsInfinity(amount) || amount < 0f) throw new ArgumentOutOfRangeException(nameof(amount));
            if (pellet < -1 || pellet > 30) throw new ArgumentOutOfRangeException(nameof(pellet));
            if (amount == 0f) return false;
            var key = new ShotKey(source, sequence);
            uint bit = 1u << (pellet < 0 ? 31 : pellet);
            if (shots.TryGetValue(key, out uint recorded))
            {
                if ((recorded & bit) != 0) return false;
            }
            else
            {
                if (retiredShot.TryGetValue(source, out int retired) && unchecked(sequence - retired) <= 0) return false;
                if (newestShot.TryGetValue(source, out int newest))
                {
                    int age = unchecked(sequence - newest);
                    // A retired old contact cannot become a new wound after eviction.
                    // Signed serial arithmetic also permits the sequence counter to wrap.
                    if (age <= -ShotHistory) return false;
                    if (age > 0) newestShot[source] = sequence;
                }
                else newestShot.Add(source, sequence);
                if (shotOrder.Count == ShotHistory)
                {
                    ShotKey old = shotOrder.Dequeue();
                    shots.Remove(old);
                    if (!retiredShot.TryGetValue(old.Source, out int previous) || unchecked(old.Sequence - previous) > 0)
                        retiredShot[old.Source] = old.Sequence;
                }
                shotOrder.Enqueue(key);
            }
            shots[key] = recorded | bit;
            float next = Math.Min(SeparationTrauma, trauma[index] + amount);
            if (next <= trauma[index]) return false;
            trauma[index] = next;
            if (next >= SeparationTrauma) Detach((int)region);
            return true;
        }

        public void Reset()
        {
            Array.Clear(trauma, 0, trauma.Length);
            Array.Clear(limbHealthSpent, 0, limbHealthSpent.Length);
            for (int i = 0; i < attached.Length; i++) attached[i] = true;
            shots.Clear(); shotOrder.Clear(); newestShot.Clear(); retiredShot.Clear();
        }

        private bool LegFunctional(bool left) => IsFunctional(left ? BodyDamageRegion.LeftThigh : BodyDamageRegion.RightThigh) &&
            IsFunctional(left ? BodyDamageRegion.LeftShin : BodyDamageRegion.RightShin) &&
            IsFunctional(left ? BodyDamageRegion.LeftFoot : BodyDamageRegion.RightFoot);
        private void Detach(int region)
        {
            attached[region] = false;
            for (int child = 0; child < RegionCount; child++) if (Parents[child] == region && attached[child]) Detach(child);
        }
        private static int RegionIndex(BodyDamageRegion region)
        {
            int index = (int)region;
            if (index < 0 || index >= RegionCount) throw new ArgumentOutOfRangeException(nameof(region));
            return index;
        }
        private static int Index(BodyDamageRegion region, int patch)
        {
            if (patch < 0 || patch >= PatchCount) throw new ArgumentOutOfRangeException(nameof(patch));
            return RegionIndex(region) * PatchCount + patch;
        }
        private readonly struct ShotKey : IEquatable<ShotKey>
        {
            private readonly int source, sequence;
            internal int Source => source;
            internal int Sequence => sequence;
            internal ShotKey(int source, int sequence) { this.source = source; this.sequence = sequence; }
            public bool Equals(ShotKey other) => source == other.source && sequence == other.sequence;
            public override bool Equals(object other) => other is ShotKey key && Equals(key);
            public override int GetHashCode() => unchecked(source * 397 ^ sequence);
        }
    }
}
