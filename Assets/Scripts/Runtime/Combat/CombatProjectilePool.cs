using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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
        internal const float TrailLifetime = .075f;
        private const float TrailLength = 1.8f;
        private const int Capacity = 16;
        private readonly Projectile[] slots = new Projectile[Capacity];
        private readonly List<Impact> contacts = new List<Impact>(Capacity);
        private RaycastHit[] casts = new RaycastHit[32];
        private Collider[] overlaps = new Collider[32];

        private readonly struct WorldHit
        {
            internal readonly Collider Surface;
            internal readonly float Fraction;
            internal readonly Vector3 Point, Normal;
            internal WorldHit(Collider surface, float fraction, Vector3 point, Vector3 normal)
            { Surface = surface; Fraction = fraction; Point = point; Normal = normal; }
        }

        private sealed class Projectile
        {
            internal GameObject Model;
            internal LineRenderer Trail;
            internal float TrailAge;
            internal int TrailFrame;
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
        public CombatSurfaceImpactEffects SurfaceEffects { get; }
        public bool HasCapacity => ActiveCount < Capacity;
        internal int VisibleTrailCount
        {
            get { int count = 0; foreach (Projectile p in slots) if (p.Trail.enabled) count++; return count; }
        }
        internal Vector3 LastTrailStart { get; private set; }
        internal Vector3 LastTrailEnd { get; private set; }

        public CombatProjectilePool(Transform parent)
        {
            var holder = new GameObject("Combat Projectiles");
            holder.transform.SetParent(parent, false);
            SurfaceEffects = holder.AddComponent<CombatSurfaceImpactEffects>();
            SurfaceEffects.Initialize();
            for (int i = 0; i < slots.Length; i++)
            {
                GameObject model = CombatPistolAssetProvider.CreateBullet(holder.transform);
                model.SetActive(false);
                var trace = new GameObject("Bullet flight streak");
                trace.transform.SetParent(holder.transform, false);
                LineRenderer trail = trace.AddComponent<LineRenderer>();
                trail.sharedMaterial = CityNightResources.AtmosphereMaterial;
                trail.useWorldSpace = true; trail.positionCount = 2;
                trail.startWidth = .025f; trail.endWidth = .065f;
                trail.startColor = new Color(2.4f, 1.3f, .36f, .8f);
                trail.endColor = new Color(4f, 3.1f, 1.4f, 1f);
                trail.alignment = LineAlignment.View;
                trail.textureMode = LineTextureMode.Stretch;
                trail.shadowCastingMode = ShadowCastingMode.Off; trail.receiveShadows = false;
                trail.lightProbeUsage = LightProbeUsage.Off;
                trail.reflectionProbeUsage = ReflectionProbeUsage.Off;
                trail.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                trail.enabled = false;
                slots[i] = new Projectile { Model = model, Trail = trail };
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
                p.Trail.enabled = false;
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
            return WorldOverlap(source, muzzle) == null && !WorldSegment(source, from, muzzle, out _) &&
                !WorldSegment(source, source.Ragdoll.PhysicsController.ChestBody.position, muzzle, out _);
        }

        internal void Advance(float seconds, CombatActor hero, CombatActor opponent)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            contacts.Clear();
            if (seconds == 0f) return;
            SurfaceEffects.Tick(seconds);
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
                bool world = WorldSegment(p.Source, from, to, out WorldHit worldHit);
                CombatActor target = p.Source == hero ? opponent : hero;
                CombatHurtboxes.Hit hit = default;
                bool body = target != null && target.Hurtboxes != null &&
                    target.Hurtboxes.SweepProjectile(from, to, Radius, p.Velocity.normalized, out hit);
                if (body && (!world || hit.Fraction < worldHit.Fraction))
                {
                    p.Position = hit.Point;
                    contacts.Add(new Impact(p, target, hit));
                    ImpactCount++; LastImpactPoint = hit.Point;
                    Retire(p);
                }
                else if (world)
                {
                    p.Position = worldHit.Point;
                    ImpactCount++; LastImpactPoint = worldHit.Point;
                    SurfaceEffects.Emit(worldHit.Surface, worldHit.Point, worldHit.Normal, p.Velocity);
                    Retire(p);
                }
                else
                {
                    p.Position = to; p.Age += dt; p.Distance += travel;
                    p.Model.transform.SetPositionAndRotation(to, Quaternion.LookRotation(p.Velocity));
                    if (p.Age + .000001f >= MaximumLifetime || p.Distance + .00001f >= MaximumDistance) Retire(p);
                }
                ShowFlightSegment(p, from, p.Position);
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

        private void ShowFlightSegment(Projectile p, Vector3 from, Vector3 to)
        {
            Vector3 travel = to - from;
            if (travel.sqrMagnitude < .000001f) return;
            Vector3 start = to - Vector3.ClampMagnitude(travel, TrailLength);
            p.Trail.SetPosition(0, start); p.Trail.SetPosition(1, to);
            p.Trail.startColor = new Color(2.4f, 1.3f, .36f, .8f);
            p.Trail.endColor = new Color(4f, 3.1f, 1.4f, 1f);
            p.TrailAge = 0f; p.TrailFrame = Time.frameCount;
            p.Trail.enabled = true;
            LastTrailStart = start; LastTrailEnd = to;
        }

        // Render lifetime is independent of flight lifetime: a close hit can
        // spawn and retire between two frames. Only actual swept travel is shown.
        internal void AdvancePresentation(float seconds)
        {
            if (seconds <= 0f) return;
            foreach (Projectile p in slots)
            {
                if (!p.Trail.enabled || Time.frameCount <= p.TrailFrame + 1) continue;
                p.TrailAge += seconds;
                float alpha = Mathf.Clamp01(1f - p.TrailAge / TrailLifetime);
                p.Trail.startColor = new Color(2.4f, 1.3f, .36f, .8f * alpha);
                p.Trail.endColor = new Color(4f, 3.1f, 1.4f, alpha);
                if (p.TrailAge >= TrailLifetime) p.Trail.enabled = false;
            }
        }

        internal void ClearFlights()
        {
            foreach (Projectile p in slots) Retire(p);
            contacts.Clear();
        }

        public void Clear()
        {
            ClearFlights();
            if (SurfaceEffects != null) SurfaceEffects.Clear();
            foreach (Projectile p in slots) if (p.Trail != null) p.Trail.enabled = false;
        }

        public void ResetRound()
        {
            Clear();
            SpawnCount = ImpactCount = 0;
            LastPosition = LastImpactPoint = Vector3.zero;
            LastTrailStart = LastTrailEnd = Vector3.zero;
        }

        private void Retire(Projectile p)
        {
            if (!p.Active) return;
            p.Active = false;
            p.Source = null;
            if (p.Model != null) p.Model.SetActive(false);
            ActiveCount--;
        }

        private Collider WorldOverlap(CombatActor source, Vector3 point)
        {
            int count;
            while ((count = Physics.OverlapSphereNonAlloc(point, Radius, overlaps, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore)) == overlaps.Length) Array.Resize(ref overlaps, overlaps.Length * 2);
            for (int i = 0; i < count; i++)
                if (IsWorld(overlaps[i], source)) return overlaps[i];
            return null;
        }

        private bool WorldSegment(CombatActor source, Vector3 from, Vector3 to, out WorldHit contact)
        {
            contact = default;
            Vector3 delta = to - from;
            float length = delta.magnitude;
            Collider overlap = WorldOverlap(source, from);
            if (overlap != null)
            {
                Vector3 direction = length > .000001f ? delta / length : Vector3.forward;
                float reach = overlap.bounds.size.magnitude + Radius * 2f;
                // An overlapping start still stops immediately, but presentation
                // needs the actual entry surface rather than an interior point.
                if (overlap.Raycast(new Ray(from - direction * reach, direction), out RaycastHit entry, reach * 2f))
                    contact = new WorldHit(overlap, 0f, entry.point, entry.normal);
                else
                {
                    Vector3 point = overlap.ClosestPoint(from);
                    Vector3 normal = from - point;
                    contact = new WorldHit(overlap, 0f, point, normal.sqrMagnitude > .000001f ? normal.normalized : -direction);
                }
                return true;
            }
            if (length < .000001f) return false;
            float fraction = float.PositiveInfinity;
            int count;
            while ((count = Physics.SphereCastNonAlloc(from, Radius, delta / length, casts, length,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) == casts.Length)
                Array.Resize(ref casts, casts.Length * 2);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = casts[i];
                float t = hit.distance / length;
                if (!IsWorld(hit.collider, source) || t >= fraction) continue;
                fraction = t;
                contact = new WorldHit(hit.collider, t, hit.point, hit.normal);
            }
            return fraction <= 1f;
        }

        internal static bool IsWorld(Collider shape, CombatActor source) => shape != null &&
            !shape.isTrigger && (source == null || !shape.transform.IsChildOf(source.transform)) &&
            shape.GetComponentInParent<CombatActor>() == null;
        private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
