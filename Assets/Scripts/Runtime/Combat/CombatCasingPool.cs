using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Bounded scene-local spent cases, advanced by the same live clock as the pistol.</summary>
    public sealed class CombatCasingPool
    {
        public const float EjectionSeconds = .028f;
        private const int Capacity = 32;
        private const float Lifetime = 8f, Radius = .0045f;
        private sealed class Casing
        {
            internal GameObject Model;
            internal Transform Port;
            internal Vector3 Position, Velocity, Spin;
            internal Quaternion Rotation;
            internal float Age, ContactSoundCooldown;
            internal int Sequence;
            internal bool Pending, Active, Resting;
        }

        private readonly Casing[] slots = new Casing[Capacity];
        private readonly RaycastHit[] hits = new RaycastHit[32];
        private Casing lastEjected;
        private int cursor;
        public int EjectionCount { get; private set; }
        // Count clocked cue requests; the shared mixer still owns voice limits.
        internal int EjectionSoundCount { get; private set; }
        internal int ContactSoundCount { get; private set; }
        public int LastShotSequence { get; private set; }
        public Vector3 LastEjectionPosition { get; private set; }
        public Vector3 LastEjectionVelocity { get; private set; }
        public Vector3 LastPosition => lastEjected != null && lastEjected.Active ? lastEjected.Position : Vector3.zero;
        public int ActiveCount
        {
            get { int count = 0; foreach (Casing casing in slots) if (casing.Active) count++; return count; }
        }
        public int PendingCount
        {
            get { int count = 0; foreach (Casing casing in slots) if (casing.Pending) count++; return count; }
        }

        public CombatCasingPool(Transform parent)
        {
            var holder = new GameObject("Spent Pistol Cases");
            holder.transform.SetParent(parent, false);
            for (int i = 0; i < slots.Length; i++)
            {
                GameObject model = CombatPistolAssetProvider.CreateCasing(holder.transform);
                model.SetActive(false);
                slots[i] = new Casing { Model = model };
            }
        }

        internal void BeginShot(CombatActor actor)
        {
            if (actor == null || actor.PistolEjectionPort == null)
                throw new InvalidOperationException("A committed pistol shot needs its authored ejection port.");
            Casing casing = slots[cursor++ % Capacity];
            casing.Model.SetActive(false);
            casing.Port = actor.PistolEjectionPort;
            casing.Sequence = actor.Pistol.ShotSequence;
            casing.Age = 0f;
            casing.ContactSoundCooldown = 0f;
            casing.Pending = true;
            casing.Active = casing.Resting = false;
        }

        internal void Tick(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (seconds == 0f) return;
            foreach (Casing casing in slots)
            {
                if (casing.Pending)
                {
                    casing.Age += seconds;
                    if (casing.Age + .000001f < EjectionSeconds) continue;
                    if (casing.Port == null) { casing.Pending = false; continue; }
                    Eject(casing);
                }
                if (!casing.Active) continue;
                casing.Age += seconds;
                casing.ContactSoundCooldown = Mathf.Max(0f, casing.ContactSoundCooldown - seconds);
                if (casing.Age >= Lifetime)
                {
                    casing.Active = false;
                    casing.Model.SetActive(false);
                    continue;
                }
                if (casing.Resting) continue;
                Vector3 next = casing.Position + casing.Velocity * seconds + Vector3.down * (4.905f * seconds * seconds);
                Vector3 travel = next - casing.Position;
                casing.Velocity += Vector3.down * (9.81f * seconds);
                float distance = travel.magnitude;
                if (distance > .000001f)
                {
                    int count = Physics.SphereCastNonAlloc(casing.Position, Radius, travel / distance,
                        hits, distance, ~0, QueryTriggerInteraction.Ignore);
                    float nearest = float.PositiveInfinity;
                    RaycastHit contact = default;
                    for (int hit = 0; hit < count; hit++)
                    {
                        RaycastHit candidate = hits[hit];
                        if (candidate.collider.GetComponentInParent<CombatActor>() != null || candidate.distance >= nearest) continue;
                        nearest = candidate.distance;
                        contact = candidate;
                    }
                    if (!float.IsPositiveInfinity(nearest))
                    {
                        next = contact.point + contact.normal * (Radius + .001f);
                        float impactSpeed = Mathf.Max(0f, -Vector3.Dot(casing.Velocity, contact.normal));
                        if (impactSpeed >= .35f && casing.ContactSoundCooldown <= 0f)
                        {
                            RetroAudio.PlayAt(RetroSfxId.PistolCasingBounce, contact.point,
                                Mathf.Lerp(.15f, .8f, Mathf.InverseLerp(.35f, 5f, impactSpeed)));
                            ContactSoundCount++;
                            casing.ContactSoundCooldown = .06f;
                        }
                        casing.Velocity = Vector3.Reflect(casing.Velocity, contact.normal) * .38f;
                        casing.Spin *= .45f;
                        if (contact.normal.y > .65f && casing.Velocity.sqrMagnitude < .36f)
                        {
                            casing.Velocity = casing.Spin = Vector3.zero;
                            casing.Resting = true;
                        }
                    }
                }
                casing.Position = next;
                casing.Rotation = Quaternion.Euler(casing.Spin * seconds) * casing.Rotation;
                casing.Model.transform.SetPositionAndRotation(casing.Position, casing.Rotation);
            }
        }

        private void Eject(Casing casing)
        {
            Transform port = casing.Port;
            // Sequence-derived variations are reproducible and allocate no per-shot objects.
            float variation = (uint)casing.Sequence % 7 / 6f;
            casing.Position = port.position + port.right * .006f;
            casing.Velocity = port.right * Mathf.Lerp(3.8f, 5f, variation) + port.up * 2.1f - port.forward * .7f;
            casing.Spin = port.forward * 720f + port.up * Mathf.Lerp(430f, 850f, variation);
            casing.Rotation = port.rotation;
            casing.Age = 0f;
            casing.Pending = false;
            casing.Active = true;
            casing.Model.transform.SetPositionAndRotation(casing.Position, casing.Rotation);
            casing.Model.SetActive(true);
            RetroAudio.PlayAt(RetroSfxId.PistolCasingEject, casing.Position, .65f);
            EjectionSoundCount++;
            EjectionCount++;
            LastShotSequence = casing.Sequence;
            LastEjectionPosition = casing.Position;
            LastEjectionVelocity = casing.Velocity;
            lastEjected = casing;
        }

        public void Clear()
        {
            foreach (Casing casing in slots)
            {
                casing.Pending = casing.Active = casing.Resting = false;
                casing.Port = null;
                if (casing.Model != null) casing.Model.SetActive(false);
            }
            lastEjected = null;
        }

        public void ResetRound()
        {
            Clear();
            cursor = EjectionCount = LastShotSequence = 0;
            EjectionSoundCount = ContactSoundCount = 0;
            LastEjectionPosition = LastEjectionVelocity = Vector3.zero;
        }
    }
}
