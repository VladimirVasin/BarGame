using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Scene-owned ballistic objects. A sweep covers ONLY one elapsed flight segment.
    /// New shots start moving on the following duel substep; presentation never owns damage.</summary>
    public sealed class CombatProjectilePool
    {
        public const float MuzzleSpeed = 250f;
        public const float Gravity = 9.81f;
        public const float MaximumDistance = 40f;
        public const float MaximumLifetime = 1f;
        public const float Radius = .004f;
        private const int Capacity = 16;
        private readonly Projectile[] slots = new Projectile[Capacity];
        private readonly List<Impact> contacts = new List<Impact>(Capacity);
        private RaycastHit[] casts = new RaycastHit[32];
        private Collider[] overlaps = new Collider[32];

        private sealed class Projectile
        {
            internal GameObject Model;
            internal CombatActor Source;
            internal Vector3 Position, Velocity;
            internal float Age, Distance;
            internal int Sequence;
            internal bool Active;
        }

        private readonly struct Impact
        {
            internal readonly CombatActor Source, Target;
            internal readonly CombatHurtboxes.Hit Hit;
            internal readonly Vector3 Velocity;
            internal readonly int Sequence;
            internal Impact(Projectile p, CombatActor target, CombatHurtboxes.Hit hit)
            { Source = p.Source; Target = target; Hit = hit; Velocity = p.Velocity; Sequence = p.Sequence; }
        }

        public int ActiveCount { get; private set; }
        public int SpawnCount { get; private set; }
        public int ImpactCount { get; private set; }
        public Vector3 LastPosition { get; private set; }
        public Vector3 LastImpactPoint { get; private set; }
        public bool HasCapacity => ActiveCount < Capacity;

        public CombatProjectilePool(Transform parent)
        {
            var holder = new GameObject("Combat Projectiles");
            holder.transform.SetParent(parent, false);
            for (int i = 0; i < slots.Length; i++)
            {
                GameObject model = CombatPistolAssetProvider.CreateBullet(holder.transform);
                model.SetActive(false);
                slots[i] = new Projectile { Model = model };
            }
        }

        internal bool TrySpawn(CombatActor source, Vector3 origin, Vector3 velocity, int sequence)
        {
            if (source == null || !Finite(origin) || !Finite(velocity) || velocity.sqrMagnitude <= .000001f) return false;
            foreach (Projectile p in slots)
            {
                if (p.Active) continue;
                p.Source = source; p.Position = origin; p.Velocity = velocity; p.Sequence = sequence;
                p.Age = p.Distance = 0f; p.Active = true;
                p.Model.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(velocity));
                p.Model.SetActive(true);
                ActiveCount++; SpawnCount++; LastPosition = origin;
                return true;
            }
            return false;
        }

        internal bool MuzzleIsClear(CombatActor source, Vector3 muzzle)
        {
            Vector3 from = source.Weapon.transform.position;
            return !WorldOverlap(source, muzzle) && !WorldSegment(source, from, muzzle, out _, out _) &&
                !WorldSegment(source, source.Ragdoll.PhysicsController.ChestBody.position, muzzle, out _, out _);
        }

        internal void Advance(float seconds, CombatActor hero, CombatActor opponent)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            contacts.Clear();
            if (seconds == 0f) return;
            foreach (Projectile p in slots)
            {
                if (!p.Active) continue;
                float dt = Mathf.Min(seconds, MaximumLifetime - p.Age);
                Vector3 from = p.Position;
                Vector3 acceleration = Vector3.down * Gravity;
                Vector3 to = from + p.Velocity * dt + acceleration * (.5f * dt * dt);
                float travel = Vector3.Distance(from, to);
                if (p.Distance + travel > MaximumDistance)
                {
                    float portion = (MaximumDistance - p.Distance) / Mathf.Max(.000001f, travel);
                    dt *= portion;
                    to = from + p.Velocity * dt + acceleration * (.5f * dt * dt);
                    travel = Vector3.Distance(from, to);
                }
                p.Velocity += acceleration * dt;
                bool world = WorldSegment(p.Source, from, to, out float worldFraction, out Vector3 worldPoint);
                CombatActor target = p.Source == hero ? opponent : hero;
                CombatHurtboxes.Hit hit = default;
                bool body = target != null && target.Hurtboxes != null &&
                    target.Hurtboxes.SweepSphere(from, to, Radius, p.Velocity.normalized, out hit);
                if (body && (!world || hit.Fraction < worldFraction))
                {
                    p.Position = hit.Point;
                    contacts.Add(new Impact(p, target, hit));
                    ImpactCount++; LastImpactPoint = hit.Point;
                    Retire(p);
                }
                else if (world)
                {
                    p.Position = worldPoint;
                    ImpactCount++; LastImpactPoint = worldPoint;
                    Retire(p);
                }
                else
                {
                    p.Position = to; p.Age += dt; p.Distance += travel;
                    p.Model.transform.SetPositionAndRotation(to, Quaternion.LookRotation(p.Velocity));
                    if (p.Age + .000001f >= MaximumLifetime || p.Distance + .00001f >= MaximumDistance) Retire(p);
                }
                LastPosition = p.Position;
            }
        }

        internal void ApplyContacts()
        {
            foreach (Impact impact in contacts)
                if (impact.Target != null)
                    impact.Target.ReceiveProjectile(impact.Source, impact.Sequence, impact.Hit, impact.Velocity);
            contacts.Clear();
        }

        public void Clear()
        {
            foreach (Projectile p in slots) Retire(p);
            contacts.Clear();
        }

        public void ResetRound()
        {
            Clear();
            SpawnCount = ImpactCount = 0;
            LastPosition = LastImpactPoint = Vector3.zero;
        }

        private void Retire(Projectile p)
        {
            if (!p.Active) return;
            p.Active = false;
            p.Source = null;
            if (p.Model != null) p.Model.SetActive(false);
            ActiveCount--;
        }

        private bool WorldOverlap(CombatActor source, Vector3 point)
        {
            int count;
            while ((count = Physics.OverlapSphereNonAlloc(point, Radius, overlaps, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore)) == overlaps.Length) Array.Resize(ref overlaps, overlaps.Length * 2);
            for (int i = 0; i < count; i++)
                if (IsWorld(overlaps[i], source)) return true;
            return false;
        }

        private bool WorldSegment(CombatActor source, Vector3 from, Vector3 to, out float fraction, out Vector3 point)
        {
            fraction = float.PositiveInfinity; point = to;
            if (WorldOverlap(source, from)) { fraction = 0f; point = from; return true; }
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < .000001f) return false;
            int count;
            while ((count = Physics.SphereCastNonAlloc(from, Radius, delta / length, casts, length,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) == casts.Length)
                Array.Resize(ref casts, casts.Length * 2);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = casts[i];
                float t = hit.distance / length;
                if (!IsWorld(hit.collider, source) || t >= fraction) continue;
                fraction = t; point = hit.point;
            }
            return fraction <= 1f;
        }

        private static bool IsWorld(Collider shape, CombatActor source) => shape != null &&
            !shape.isTrigger && (source == null || !shape.transform.IsChildOf(source.transform)) &&
            shape.GetComponentInParent<CombatActor>() == null;
        private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
