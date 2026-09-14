using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public sealed class CityLitterItem
    {
        internal CityLitterItem(string name, string category, bool solid, Bounds bounds, int triangles)
        { Name = name; Category = category; Solid = solid; Bounds = bounds; TriangleCount = triangles; }
        public string Name { get; }
        public string Category { get; }
        public bool Solid { get; }
        public Bounds Bounds { get; }
        public int TriangleCount { get; }
    }

    /// <summary>Measured Blender dimensions, shared by placement, import validation and map clearance.</summary>
    public sealed class CityLitterCatalog
    {
        public const string ResourcePath = "City/EastExit/CityLitter3D";
        private static CityLitterCatalog current;
        public IReadOnlyList<CityLitterItem> Items { get; }

        private CityLitterCatalog(Manifest source)
        {
            if (source == null || source.schema_version != 1 || source.items == null || source.items.Length == 0)
                throw new InvalidOperationException("Missing or unsupported City litter manifest.");
            var items = new List<CityLitterItem>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ItemRecord record in source.items)
            {
                if (string.IsNullOrEmpty(record.name) || !names.Add(record.name) ||
                    string.IsNullOrEmpty(record.category) || record.triangle_count <= 0 ||
                    record.bounds_min_unity == null || record.bounds_min_unity.Length != 3 ||
                    record.bounds_max_unity == null || record.bounds_max_unity.Length != 3)
                    throw new InvalidOperationException("Invalid City litter catalog entry: " + record.name);
                Vector3 low = Vector(record.bounds_min_unity), high = Vector(record.bounds_max_unity);
                Vector3 size = high - low;
                if (size.x <= 0 || size.y <= 0 || size.z <= 0 || size.magnitude > 4f ||
                    Mathf.Abs(low.y) > .005f || Mathf.Abs(low.x + high.x) > .005f || Mathf.Abs(low.z + high.z) > .005f)
                    throw new InvalidOperationException("Litter must be centred, grounded and authored in metres: " + record.name);
                items.Add(new CityLitterItem(record.name, record.category, record.solid,
                    new Bounds((low + high) * .5f, size), record.triangle_count));
            }
            Items = new ReadOnlyCollection<CityLitterItem>(items);
        }

        public static CityLitterCatalog Load()
        {
            if (current != null) return current;
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null) throw new InvalidOperationException("Missing measured litter catalog: " + ResourcePath);
            return current = new CityLitterCatalog(JsonUtility.FromJson<Manifest>(asset.text));
        }

        private static Vector3 Vector(float[] value) => new Vector3(value[0], value[1], value[2]);

        [Serializable] private sealed class Manifest
        {
            public int schema_version;
            public ItemRecord[] items;
        }
        [Serializable] private sealed class ItemRecord
        {
            public string name, category;
            public bool solid;
            public float[] bounds_min_unity, bounds_max_unity;
            public int triangle_count;
        }
    }
}
