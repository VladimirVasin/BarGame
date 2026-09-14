using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum CityLitterZone
    {
        Sidewalk = 0,
        LotGround = 1,
        Park = 2,
        Beach = 3
    }

    public readonly struct CityLitterPart
    {
        internal CityLitterPart(string id, CityLitterItem item, CityLitterZone zone, CityDistrictKind district,
            Vector2Int cell, int surface, Vector3 position, Quaternion rotation, float scale, Rect footprint)
        {
            Id = id; Item = item; Zone = zone; District = district; Cell = cell; Surface = surface;
            Position = position; Rotation = rotation; Scale = scale; Footprint = footprint;
        }

        public string Id { get; }
        public CityLitterItem Item { get; }
        public CityLitterZone Zone { get; }
        public CityDistrictKind District { get; }
        public Vector2Int Cell { get; }
        /// <summary>Index into the street plan's sidewalk geometry or the layout's surfaces, by zone.</summary>
        public int Surface { get; }
        /// <summary>Plan top under the centre; the builder lifts the rigid body from its actual vertices.</summary>
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public float Scale { get; }
        /// <summary>Conservative full projected body, including its lean and placement clearance.</summary>
        public Rect Footprint { get; }
    }

    /// <summary>
    /// Sparse passive litter wherever the city lets it lie: sidewalk edges,
    /// the bare band around a lot, the park's benches and the tide line
    /// (art §2.3, 2026-09-15). Pure and seeded; one per decoration plan.
    /// The eastern strip keeps its own denser <see cref="CityEastLitterPlan"/>.
    /// </summary>
    public sealed class CityLitterPlan
    {
        /// <summary>A budget, not an estimate: the planner stops adding at it.</summary>
        public const int MaximumPartCount = 720;
        /// <summary>Footprint-to-footprint gap in every zone; three times the eastern strip's.</summary>
        public const float MinimumItemClearance = 1.5f;
        public const float SmallSameVariantRadius = 18f;
        public const float SolidSameVariantRadius = 45f;
        public const float BicycleSameVariantRadius = 90f;
        /// <summary>The nearest neighbour inside this radius should be of another category.</summary>
        public const float NeighbourCategoryRadius = 6f;
        public const int MaximumSolidCount = 48;
        public const int MaximumBicycleCount = 3;
        public const int MaximumBicyclesPerDistrict = 1;
        public const string BicycleCategory = "bicycle";

        private readonly int[] zoneCounts = new int[4];
        private readonly Dictionary<CityDistrictKind, int> districtCounts = new Dictionary<CityDistrictKind, int>();

        internal CityLitterPlan(IList<CityLitterPart> parts)
        {
            if (parts == null) throw new ArgumentNullException(nameof(parts));
            if (parts.Count > MaximumPartCount)
                throw new InvalidOperationException("The city litter plan exceeds its part budget: " + parts.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (CityLitterPart part in parts)
            {
                if (string.IsNullOrEmpty(part.Id) || !ids.Add(part.Id))
                    throw new InvalidOperationException("City litter ids must be unique: " + part.Id);
                if (!IsFinite(part.Position) || part.Scale <= 0f || float.IsNaN(part.Scale))
                    throw new InvalidOperationException("City litter part is not placeable: " + part.Id);
                zoneCounts[(int)part.Zone]++;
                districtCounts.TryGetValue(part.District, out int count);
                districtCounts[part.District] = count + 1;
                TriangleCount += part.Item.TriangleCount;
                if (part.Item.Solid) SolidCount++;
            }
            Parts = new ReadOnlyCollection<CityLitterPart>(new List<CityLitterPart>(parts));
        }

        public IReadOnlyList<CityLitterPart> Parts { get; }
        public int TriangleCount { get; }
        public int SolidCount { get; }

        public int GetCount(CityLitterZone zone) => zoneCounts[(int)zone];

        public int GetCount(CityDistrictKind district) =>
            districtCounts.TryGetValue(district, out int count) ? count : 0;

        /// <summary>How far the same authored variant must stay from its twin.</summary>
        public static float SameVariantRadius(CityLitterItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (item.Category == BicycleCategory) return BicycleSameVariantRadius;
            return item.Solid ? SolidSameVariantRadius : SmallSameVariantRadius;
        }

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) &&
            !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
