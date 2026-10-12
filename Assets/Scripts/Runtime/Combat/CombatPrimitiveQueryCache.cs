using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Enabled native primitive geometry for queries against disabled posed anatomy.
    /// Copies stay remote; callers always supply the source's live or sampled world pose.</summary>
    internal sealed class CombatPrimitiveQueryCache : IDisposable
    {
        private readonly Dictionary<Collider, Entry> entries = new Dictionary<Collider, Entry>();
        private GameObject root;
        internal int Count => entries.Count;

        internal Collider Prepare(Collider source)
        {
            if (source == null || !(source is CapsuleCollider || source is BoxCollider || source is SphereCollider))
                return source;
            var geometry = new Geometry(source);
            if (!entries.TryGetValue(source, out Entry entry))
            {
                if (root == null)
                {
                    root = new GameObject("Combat primitive query geometry") { hideFlags = HideFlags.HideAndDontSave };
                    root.transform.position = Vector3.up * 10000f;
                }
                var copy = new GameObject("Combat primitive query") { hideFlags = HideFlags.HideAndDontSave };
                copy.transform.SetParent(root.transform, false);
                Collider native = source is CapsuleCollider ? copy.AddComponent<CapsuleCollider>() :
                    source is BoxCollider ? copy.AddComponent<BoxCollider>() : (Collider)copy.AddComponent<SphereCollider>();
                native.isTrigger = true;
                native.enabled = true;
                entry = new Entry(native);
                entries.Add(source, entry);
            }
            if (!entry.Prepared || !entry.Geometry.Matches(geometry))
            {
                geometry.Apply(entry.Native);
                entry.Geometry = geometry;
                entry.Prepared = true;
            }
            return entry.Native;
        }

        internal void Clear()
        {
            entries.Clear();
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
        }

        public void Dispose() => Clear();

        private sealed class Entry
        {
            internal readonly Collider Native;
            internal Geometry Geometry;
            internal bool Prepared;
            internal Entry(Collider native) { Native = native; }
        }

        private readonly struct Geometry
        {
            private readonly Vector3 scale, center, size;
            private readonly float radius, height;
            private readonly int direction;

            internal Geometry(Collider source)
            {
                scale = source.transform.lossyScale;
                center = size = default; radius = height = 0f; direction = 0;
                if (source is CapsuleCollider capsule)
                { center = capsule.center; radius = capsule.radius; height = capsule.height; direction = capsule.direction; }
                else if (source is BoxCollider box)
                { center = box.center; size = box.size; }
                else if (source is SphereCollider sphere)
                { center = sphere.center; radius = sphere.radius; }
            }

            internal bool Matches(Geometry other) => scale.Equals(other.scale) && center.Equals(other.center) &&
                size.Equals(other.size) && radius.Equals(other.radius) && height.Equals(other.height) && direction == other.direction;

            internal void Apply(Collider native)
            {
                // Keep remote transforms at unit scale. Geometry setters update
                // native shapes without a global SyncTransforms on each solve.
                Vector3 absolute = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                Vector3 scaledCenter = Vector3.Scale(center, scale);
                if (native is CapsuleCollider capsule)
                {
                    capsule.direction = direction;
                    capsule.center = scaledCenter;
                    capsule.radius = radius * Mathf.Max(absolute[(direction + 1) % 3], absolute[(direction + 2) % 3]);
                    capsule.height = Mathf.Max(height * absolute[direction], capsule.radius * 2f);
                }
                else if (native is BoxCollider box)
                { box.center = scaledCenter; box.size = Vector3.Scale(size, absolute); }
                else if (native is SphereCollider sphere)
                { sphere.center = scaledCenter; sphere.radius = radius * Mathf.Max(absolute.x, Mathf.Max(absolute.y, absolute.z)); }
            }
        }
    }
}
