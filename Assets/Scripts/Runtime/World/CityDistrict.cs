using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum CityDistrictKind
    {
        OldTown = 0,
        Residential = 1,
        Industrial = 2,
        Nightlife = 3,
        CentralPark = 4,
        NorthWaterfront = 5,
        // 6 was the lake, drained when its boat station moved to the
        // seacoast. The value stays a hole so nothing renumbers.
        Cemetery = 7,
        Yard = 8
    }

    public enum CityLandUseKind
    {
        Building = 0,
        Park = 1,
        DistrictPointOfInterest = 2
    }

    public enum CityPathKind
    {
        Street = 0,
        ParkPath = 1
    }

    public sealed class CityDistrictDescriptor
    {
        private readonly HashSet<Vector2Int> cellSet;

        internal CityDistrictDescriptor(
            string areaId,
            CityDistrictKind kind,
            IList<Vector2Int> cells,
            Bounds worldBounds)
        {
            if (string.IsNullOrWhiteSpace(areaId))
            {
                throw new ArgumentException(
                    "A district descriptor requires a stable area ID.",
                    nameof(areaId));
            }

            AreaId = areaId;
            Kind = kind;
            Cells = new ReadOnlyCollection<Vector2Int>(
                new List<Vector2Int>(
                    cells ?? throw new ArgumentNullException(nameof(cells))));
            WorldBounds = worldBounds;
            cellSet = new HashSet<Vector2Int>(Cells);
        }

        public string AreaId { get; }
        public CityDistrictKind Kind { get; }
        public IReadOnlyList<Vector2Int> Cells { get; }
        public Bounds WorldBounds { get; }
        public Vector3 CenterWorldPosition => WorldBounds.center;

        public bool ContainsCell(Vector2Int cell)
        {
            return cellSet.Contains(cell);
        }
    }

    public readonly struct CityParkGateDescriptor :
        IEquatable<CityParkGateDescriptor>
    {
        internal CityParkGateDescriptor(
            string id,
            Vector3 center,
            Vector3 outwardNormal,
            float width)
        {
            Id = id ?? string.Empty;
            Center = center;
            OutwardNormal = outwardNormal;
            Width = width;
        }

        public string Id { get; }
        public Vector3 Center { get; }
        public Vector3 OutwardNormal { get; }
        public float Width { get; }
        public bool IsHorizontal => Mathf.Abs(OutwardNormal.z) > 0.5f;

        public bool Equals(CityParkGateDescriptor other)
        {
            return string.Equals(Id, other.Id, StringComparison.Ordinal) &&
                   Center.Equals(other.Center) &&
                   OutwardNormal.Equals(other.OutwardNormal) &&
                   Width.Equals(other.Width);
        }

        public override bool Equals(object obj)
        {
            return obj is CityParkGateDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(Id);
                hash = (hash * 397) ^ Center.GetHashCode();
                hash = (hash * 397) ^ OutwardNormal.GetHashCode();
                return (hash * 397) ^ Width.GetHashCode();
            }
        }
    }

    public sealed class CityParkPlan
    {
        private readonly HashSet<Vector2Int> cellSet;

        internal CityParkPlan(
            IList<Vector2Int> cells,
            Rect walkableBounds,
            Vector3 center,
            IList<CityParkGateDescriptor> gates,
            IList<Vector3> treePositions,
            IList<Vector3> benchPositions)
            : this(
                cells,
                walkableBounds,
                center,
                gates,
                treePositions,
                benchPositions,
                cells != null && cells.Count > 0
                    ? new[]
                    {
                        new CityParkRegionPlan(
                            "central-park",
                            cells,
                            walkableBounds,
                            center,
                            gates,
                            center)
                    }
                    : Array.Empty<CityParkRegionPlan>())
        {
        }

        internal CityParkPlan(
            IList<Vector2Int> cells,
            Rect walkableBounds,
            Vector3 center,
            IList<CityParkGateDescriptor> gates,
            IList<Vector3> treePositions,
            IList<Vector3> benchPositions,
            IList<CityParkRegionPlan> regions)
        {
            Cells = new ReadOnlyCollection<Vector2Int>(
                new List<Vector2Int>(
                    cells ?? throw new ArgumentNullException(nameof(cells))));
            WalkableBounds = walkableBounds;
            Center = center;
            Gates = new ReadOnlyCollection<CityParkGateDescriptor>(
                new List<CityParkGateDescriptor>(
                    gates ?? throw new ArgumentNullException(nameof(gates))));
            TreePositions = new ReadOnlyCollection<Vector3>(
                new List<Vector3>(
                    treePositions ??
                    throw new ArgumentNullException(nameof(treePositions))));
            BenchPositions = new ReadOnlyCollection<Vector3>(
                new List<Vector3>(
                    benchPositions ??
                    throw new ArgumentNullException(nameof(benchPositions))));
            Regions = new ReadOnlyCollection<CityParkRegionPlan>(
                new List<CityParkRegionPlan>(
                    regions ?? throw new ArgumentNullException(nameof(regions))));
            cellSet = new HashSet<Vector2Int>(Cells);
        }

        public IReadOnlyList<Vector2Int> Cells { get; }
        public Rect WalkableBounds { get; }
        public Vector3 Center { get; }
        public IReadOnlyList<CityParkGateDescriptor> Gates { get; }
        public IReadOnlyList<Vector3> TreePositions { get; }
        public IReadOnlyList<Vector3> BenchPositions { get; }
        public IReadOnlyList<CityParkRegionPlan> Regions { get; }
        public bool IsEnabled => Cells.Count > 0;

        public bool ContainsCell(Vector2Int cell)
        {
            return cellSet.Contains(cell);
        }
    }

    public sealed class CityParkRegionPlan
    {
        private readonly HashSet<Vector2Int> cellSet;

        internal CityParkRegionPlan(
            string id,
            IList<Vector2Int> cells,
            Rect walkableBounds,
            Vector3 center,
            IList<CityParkGateDescriptor> gates,
            Vector3 plazaPosition)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "A park region requires a stable ID.",
                    nameof(id));
            }

            Id = id.Trim();
            Cells = new ReadOnlyCollection<Vector2Int>(
                new List<Vector2Int>(
                    cells ?? throw new ArgumentNullException(nameof(cells))));
            WalkableBounds = walkableBounds;
            Center = center;
            Gates = new ReadOnlyCollection<CityParkGateDescriptor>(
                new List<CityParkGateDescriptor>(
                    gates ?? throw new ArgumentNullException(nameof(gates))));
            PlazaPosition = plazaPosition;
            cellSet = new HashSet<Vector2Int>(Cells);
        }

        public string Id { get; }
        public IReadOnlyList<Vector2Int> Cells { get; }
        public Rect WalkableBounds { get; }
        public Vector3 Center { get; }
        public IReadOnlyList<CityParkGateDescriptor> Gates { get; }
        public Vector3 PlazaPosition { get; }

        public bool ContainsCell(Vector2Int cell)
        {
            return cellSet.Contains(cell);
        }
    }
}
