using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Cloth contacts with explicitly registered equipment on this rig. Dropped props are excluded.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerEquipmentContacts : MonoBehaviour
    {
        internal uint SnapshotVersion { get; private set; }
        private const float Padding = .004f;
        private sealed class Envelope
        {
            public Transform Transform;
            public Bounds LocalBounds, WorldBounds;
            public Matrix4x4 ToWorld, ToLocal;
            public Vector3 Padding;
            public Transform[] ExcludedRoots;
            public bool Active;
        }

        private sealed class Ribbon
        {
            public Vector3[] Centres;
            public Envelope[] Segments;
            public float HalfWidth, HalfThickness;
            public bool Dirty;
        }

        private readonly Dictionary<object, Envelope[]> registrations = new Dictionary<object, Envelope[]>();
        private readonly Dictionary<object, Ribbon> ribbons = new Dictionary<object, Ribbon>();
        private Bounds worldBounds;
        private bool hasBounds;

        // Current posed boxes in the same order as the managed point solver.
        // Cloth can copy them once for a whole synchronous surface solve.
        internal struct BoxSnapshot
        {
            public Vector3 WorldMinimum, WorldMaximum, LocalCentre, LocalExtent, Padding;
            public Matrix4x4 ToWorld, ToLocal;
        }

        internal bool TryGetSnapshotBounds(out Bounds bounds)
        {
            bounds = worldBounds;
            return hasBounds;
        }

        internal int BoxSnapshotCount(bool includeRibbons)
        {
            int count = 0;
            foreach (Envelope[] group in registrations.Values)
                foreach (Envelope envelope in group) if (envelope.Active) count++;
            if (includeRibbons)
                foreach (Ribbon ribbon in ribbons.Values)
                    foreach (Envelope envelope in ribbon.Segments) if (envelope.Active) count++;
            return count;
        }

        internal void CaptureBakeSnapshot(Transform frame, float[] destination, int offset)
        {
            Matrix4x4 toFrame = frame.worldToLocalMatrix;
            foreach (Envelope[] group in registrations.Values)
                foreach (Envelope envelope in group)
                {
                    if (!envelope.Active) continue;
                    Matrix4x4 matrix = toFrame * envelope.ToWorld;
                    for (int i = 0; i < 16; i++) destination[offset++] = matrix[i];
                    Put(envelope.LocalBounds.center); Put(envelope.LocalBounds.extents); Put(envelope.Padding);
                }
            void Put(Vector3 value)
            { destination[offset++] = value.x; destination[offset++] = value.y; destination[offset++] = value.z; }
        }

        internal string BakeSnapshotName(int index)
        {
            foreach (Envelope[] group in registrations.Values)
                foreach (Envelope envelope in group)
                    if (envelope.Active && index-- == 0) return envelope.Transform.name;
            return "unknown";
        }

        internal int CopyBoxSnapshots(NativeArray<BoxSnapshot> destination, bool includeRibbons)
        {
            int count = 0;
            foreach (Envelope[] group in registrations.Values)
                foreach (Envelope envelope in group) Copy(envelope);
            if (includeRibbons)
                foreach (Ribbon ribbon in ribbons.Values)
                    foreach (Envelope envelope in ribbon.Segments) Copy(envelope);
            return count;

            void Copy(Envelope envelope)
            {
                if (!envelope.Active) return;
                if (count >= destination.Length) throw new ArgumentException("Equipment snapshot buffer is too small.", nameof(destination));
                destination[count++] = new BoxSnapshot
                {
                    WorldMinimum = envelope.WorldBounds.min, WorldMaximum = envelope.WorldBounds.max,
                    LocalCentre = envelope.LocalBounds.center, LocalExtent = envelope.LocalBounds.extents + envelope.Padding,
                    Padding = envelope.Padding, ToWorld = envelope.ToWorld, ToLocal = envelope.ToLocal
                };
            }
        }

        public void Register(object owner, params GameObject[] models)
            => Register(owner, Array.Empty<Transform>(), models);

        public void Register(object owner, Transform[] excludedRoots, params GameObject[] models)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            var envelopes = new List<Envelope>();
            var seen = new HashSet<MeshFilter>();
            foreach (GameObject model in models)
            {
                if (model == null) continue;
                foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null || !seen.Add(filter) ||
                        filter.name == "Ribbon__Webbing" ||
                        filter.name.IndexOf("Flash", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    envelopes.Add(new Envelope { Transform = filter.transform, LocalBounds = filter.sharedMesh.bounds,
                        ExcludedRoots = excludedRoots ?? Array.Empty<Transform>() });
                }
            }
            registrations[owner] = envelopes.ToArray();
            UpdatePose();
        }

        public void Unregister(object owner)
        {
            if (owner != null) registrations.Remove(owner);
            if (owner != null) ribbons.Remove(owner);
            UpdatePose();
        }

        /// <summary>Uses the solved strap centreline, never the empty space enclosed by its loop.</summary>
        public void SetRibbonContacts(object owner, Vector3[] worldCentres, float halfWidth, float halfThickness)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (worldCentres == null || worldCentres.Length < 2) { ClearRibbonContacts(owner); return; }
            if (!ribbons.TryGetValue(owner, out Ribbon ribbon))
                ribbons.Add(owner, ribbon = new Ribbon());
            if (ribbon.Segments == null || ribbon.Segments.Length != worldCentres.Length - 1)
            {
                ribbon.Segments = new Envelope[worldCentres.Length - 1];
                for (int i = 0; i < ribbon.Segments.Length; i++) ribbon.Segments[i] = new Envelope();
            }
            ribbon.Centres = worldCentres;
            ribbon.HalfWidth = Mathf.Max(0f, halfWidth);
            ribbon.HalfThickness = Mathf.Max(0f, halfThickness);
            ribbon.Dirty = true;
        }

        public void ClearRibbonContacts(object owner)
        {
            if (owner != null) ribbons.Remove(owner);
            UpdatePose();
        }

        internal void UpdatePose()
        {
            unchecked { SnapshotVersion++; }
            hasBounds = false;
            foreach (Envelope[] group in registrations.Values)
                foreach (Envelope envelope in group)
                {
                    Transform part = envelope.Transform;
                    envelope.Active = part != null && part.gameObject.activeInHierarchy && part.IsChildOf(transform);
                    if (envelope.Active)
                        foreach (Transform root in envelope.ExcludedRoots)
                            if (root != null && part.IsChildOf(root)) { envelope.Active = false; break; }
                    if (!envelope.Active) continue;
                    envelope.ToWorld = part.localToWorldMatrix;
                    envelope.ToLocal = part.worldToLocalMatrix;
                    Matrix4x4 matrix = envelope.ToWorld;
                    Vector3 extents = envelope.LocalBounds.extents;
                    Vector3 worldExtents = new Vector3(
                        Mathf.Abs(matrix.m00) * extents.x + Mathf.Abs(matrix.m01) * extents.y + Mathf.Abs(matrix.m02) * extents.z,
                        Mathf.Abs(matrix.m10) * extents.x + Mathf.Abs(matrix.m11) * extents.y + Mathf.Abs(matrix.m12) * extents.z,
                        Mathf.Abs(matrix.m20) * extents.x + Mathf.Abs(matrix.m21) * extents.y + Mathf.Abs(matrix.m22) * extents.z);
                    float clearance = Padding;
                    envelope.WorldBounds = new Bounds(matrix.MultiplyPoint3x4(envelope.LocalBounds.center),
                        (worldExtents + Vector3.one * clearance) * 2f);
                    envelope.Padding = new Vector3(
                        clearance / Mathf.Max(.000001f, matrix.MultiplyVector(Vector3.right).magnitude),
                        clearance / Mathf.Max(.000001f, matrix.MultiplyVector(Vector3.up).magnitude),
                        clearance / Mathf.Max(.000001f, matrix.MultiplyVector(Vector3.forward).magnitude));
                    if (hasBounds) worldBounds.Encapsulate(envelope.WorldBounds);
                    else { worldBounds = envelope.WorldBounds; hasBounds = true; }
                }
            foreach (Ribbon ribbon in ribbons.Values)
            {
                if (ribbon.Dirty)
                {
                    for (int i = 0; i < ribbon.Segments.Length; i++)
                    {
                        Envelope envelope = ribbon.Segments[i];
                        Vector3 direction = ribbon.Centres[i + 1] - ribbon.Centres[i];
                        float length = direction.magnitude;
                        envelope.Active = length > .00001f;
                        if (!envelope.Active) continue;
                        Vector3 centre = (ribbon.Centres[i + 1] + ribbon.Centres[i]) * .5f;
                        Vector3 outward = centre - transform.position;
                        outward.y = 0f;
                        if (outward.sqrMagnitude < .000001f) outward = transform.forward;
                        direction /= length;
                        outward.Normalize();
                        if (Mathf.Abs(Vector3.Dot(direction, outward)) > .98f)
                            outward = Mathf.Abs(direction.y) < .98f ? Vector3.up : Vector3.forward;
                        envelope.ToWorld = Matrix4x4.TRS(centre, Quaternion.LookRotation(direction, outward), Vector3.one);
                        envelope.ToLocal = envelope.ToWorld.inverse;
                        envelope.LocalBounds = new Bounds(Vector3.zero,
                            new Vector3(ribbon.HalfWidth, ribbon.HalfThickness, length * .5f) * 2f);
                        envelope.Padding = Vector3.one * Padding;
                        Vector3 extents = envelope.LocalBounds.extents + envelope.Padding;
                        Matrix4x4 matrix = envelope.ToWorld;
                        Vector3 worldExtents = new Vector3(
                            Mathf.Abs(matrix.m00) * extents.x + Mathf.Abs(matrix.m01) * extents.y + Mathf.Abs(matrix.m02) * extents.z,
                            Mathf.Abs(matrix.m10) * extents.x + Mathf.Abs(matrix.m11) * extents.y + Mathf.Abs(matrix.m12) * extents.z,
                            Mathf.Abs(matrix.m20) * extents.x + Mathf.Abs(matrix.m21) * extents.y + Mathf.Abs(matrix.m22) * extents.z);
                        envelope.WorldBounds = new Bounds(centre, worldExtents * 2f);
                    }
                    ribbon.Dirty = false;
                }
                foreach (Envelope envelope in ribbon.Segments)
                {
                    if (!envelope.Active) continue;
                    if (hasBounds) worldBounds.Encapsulate(envelope.WorldBounds);
                    else { worldBounds = envelope.WorldBounds; hasBounds = true; }
                }
            }
        }

        internal bool Contains(Vector3 point, float tolerance = 0f, bool includeRibbons = true)
        {
            if (!hasBounds || !worldBounds.Contains(point)) return false;
            foreach (Envelope[] group in registrations.Values)
                foreach (Envelope envelope in group)
                    if (Inside(envelope)) return true;
            if (includeRibbons)
                foreach (Ribbon ribbon in ribbons.Values)
                    foreach (Envelope envelope in ribbon.Segments)
                        if (Inside(envelope)) return true;
            return false;

            bool Inside(Envelope envelope)
            {
                if (!envelope.Active || !envelope.WorldBounds.Contains(point)) return false;
                Vector3 offset = envelope.ToLocal.MultiplyPoint3x4(point) - envelope.LocalBounds.center;
                Vector3 extent = envelope.LocalBounds.extents + envelope.Padding * (1f - Mathf.Max(0f, tolerance) / Padding);
                return Mathf.Abs(offset.x) < extent.x && Mathf.Abs(offset.y) < extent.y && Mathf.Abs(offset.z) < extent.z;
            }
        }

        internal bool ResolvePoint(ref Vector3 point, bool includeRibbons = true)
        {
            if (!hasBounds || !worldBounds.Contains(point)) return false;
            bool moved = false;
            foreach (Envelope[] group in registrations.Values)
                foreach (Envelope envelope in group)
                    moved |= ResolveEnvelope(envelope, ref point);
            if (includeRibbons)
                foreach (Ribbon ribbon in ribbons.Values)
                    foreach (Envelope envelope in ribbon.Segments)
                        moved |= ResolveEnvelope(envelope, ref point);
            return moved;
        }

        private bool ResolveEnvelope(Envelope envelope, ref Vector3 point)
        {
            if (!envelope.Active || !envelope.WorldBounds.Contains(point)) return false;
            Vector3 local = envelope.ToLocal.MultiplyPoint3x4(point);
            Vector3 offset = local - envelope.LocalBounds.center;
            Vector3 extent = envelope.LocalBounds.extents + envelope.Padding;
            if (Mathf.Abs(offset.x) >= extent.x || Mathf.Abs(offset.y) >= extent.y || Mathf.Abs(offset.z) >= extent.z) return false;
            float x = (extent.x - Mathf.Abs(offset.x)) / Mathf.Max(.000001f, envelope.Padding.x);
            float y = (extent.y - Mathf.Abs(offset.y)) / Mathf.Max(.000001f, envelope.Padding.y);
            float z = (extent.z - Mathf.Abs(offset.z)) / Mathf.Max(.000001f, envelope.Padding.z);
            if (x <= y && x <= z) offset.x = (offset.x < 0f ? -1f : 1f) * extent.x;
            else if (y <= z) offset.y = (offset.y < 0f ? -1f : 1f) * extent.y;
            else offset.z = (offset.z < 0f ? -1f : 1f) * extent.z;
            point = envelope.ToWorld.MultiplyPoint3x4(envelope.LocalBounds.center + offset);
            return true;

        }

    }
}
