using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum CityMountainBoundarySide
    {
        West = 0,
        South = 1
    }

    /// <summary>
    /// Stable, blueprint-owned vocabulary for the physical mountain rim.
    /// A definition is deliberately opt-in: merely placing similarly named
    /// yards in another blueprint does not silently acquire mountains.
    /// </summary>
    public sealed class CityMountainBoundaryDefinition
    {
        public const string WestSouthAreaId = "yard-west-south";
        public const string WestNorthAreaId = "yard-west-north";
        public const string SouthWestAreaId = "yard-south-west";
        public const string SouthEastAreaId = "yard-south-east";
        public const string TunnelAccessId = "yard-south-west-access";

        public const float RidgeStationSpacing = 22f;
        public const float RidgeMinimumHeight = 18f;
        public const float RidgeMaximumHeight = 27f;
        public const float RidgeMinimumDepth = 16f;
        public const float RidgeMaximumDepth = 22f;
        public const float NorthTaperHeight = 10f;

        public const float TunnelOpeningWidth = 8f;
        public const float TunnelOpeningHeight = 5.5f;
        public const float TunnelVisualDepth = 72f;
        public const float TunnelSegmentLength = 6f;
        public const float TunnelStraightDepth = 12f;
        public const float TunnelPhysicalDepth = 12f;
        public const float TunnelWalkableDepth = 11f;
        public const float TunnelDecisionDistance = 8f;
        public const float TunnelReturnDistance = 6.5f;
        public const float TunnelMapDisplayDepth = 12f;
        public const float TunnelBendDegreesPerSegment = 4f;
        public const float TunnelSightOcclusionDistance = 40f;
        public const float TunnelPortalDepth = 1.6f;

        public const float RiverCaveOpeningHeight = 8f;
        public const float RiverCaveThroatDepth = 56f;
        public const float RiverCavePortalDepth = 1.6f;

        private static readonly CityMountainBoundaryDefinition
            DefaultDefinition = new CityMountainBoundaryDefinition(
                CityBlueprintCatalog.DefaultBlueprintId);

        private CityMountainBoundaryDefinition(string blueprintId)
        {
            BlueprintId = blueprintId;
        }

        public string BlueprintId { get; }

        public static bool TryResolve(
            string blueprintId,
            out CityMountainBoundaryDefinition definition)
        {
            if (string.Equals(
                    blueprintId,
                    CityBlueprintCatalog.DefaultBlueprintId,
                    StringComparison.Ordinal))
            {
                definition = DefaultDefinition;
                return true;
            }

            definition = null;
            return false;
        }

        public static bool IsMountainFacingAreaId(string areaId)
        {
            return string.Equals(
                       areaId,
                       WestSouthAreaId,
                       StringComparison.Ordinal) ||
                   string.Equals(
                       areaId,
                       WestNorthAreaId,
                       StringComparison.Ordinal) ||
                   string.Equals(
                       areaId,
                       SouthWestAreaId,
                       StringComparison.Ordinal) ||
                   string.Equals(
                       areaId,
                       SouthEastAreaId,
                       StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// One cross-section sample of a ridge. WorldXZ is the inner toe on
    /// authoritative city ground. Moving Depth metres along OutwardNormal
    /// reaches the outer foot; the crest rises to PeakY between them.
    /// </summary>
    public readonly struct CityMountainRidgeStation :
        IEquatable<CityMountainRidgeStation>
    {
        public CityMountainRidgeStation(
            string stableId,
            Vector2 worldXZ,
            float baseY,
            float peakY,
            Vector3 outwardNormal,
            float depth,
            int seed)
        {
            StableId = stableId ?? string.Empty;
            WorldXZ = worldXZ;
            BaseY = baseY;
            PeakY = peakY;
            OutwardNormal = outwardNormal;
            Depth = depth;
            Seed = seed;
        }

        public string StableId { get; }
        public Vector2 WorldXZ { get; }
        public float BaseY { get; }
        public float PeakY { get; }
        public Vector3 OutwardNormal { get; }
        public float Depth { get; }
        public int Seed { get; }

        public Vector3 Toe => new Vector3(WorldXZ.x, BaseY, WorldXZ.y);

        public Vector3 OuterFoot => Toe + OutwardNormal * Depth;

        public bool Equals(CityMountainRidgeStation other)
        {
            return string.Equals(
                       StableId,
                       other.StableId,
                       StringComparison.Ordinal) &&
                   WorldXZ.Equals(other.WorldXZ) &&
                   BaseY.Equals(other.BaseY) &&
                   PeakY.Equals(other.PeakY) &&
                   OutwardNormal.Equals(other.OutwardNormal) &&
                   Depth.Equals(other.Depth) &&
                   Seed == other.Seed;
        }

        public override bool Equals(object obj)
        {
            return obj is CityMountainRidgeStation other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(
                    StableId ?? string.Empty);
                hash = (hash * 397) ^ WorldXZ.GetHashCode();
                hash = (hash * 397) ^ BaseY.GetHashCode();
                hash = (hash * 397) ^ PeakY.GetHashCode();
                hash = (hash * 397) ^ OutwardNormal.GetHashCode();
                hash = (hash * 397) ^ Depth.GetHashCode();
                return (hash * 397) ^ Seed;
            }
        }
    }

    /// <summary>
    /// A continuous strip of physical mountain mesh. South strips are
    /// already split around both the tunnel and river gaps, so builders
    /// must never infer or cut openings themselves.
    /// </summary>
    public sealed class CityMountainRidgeDescriptor
    {
        private readonly ReadOnlyCollection<CityMountainRidgeStation>
            stations;

        internal CityMountainRidgeDescriptor(
            string stableId,
            string sourceAreaId,
            CityMountainBoundarySide side,
            IList<CityMountainRidgeStation> sourceStations,
            bool isSouthWestJoin)
        {
            StableId = stableId ?? string.Empty;
            SourceAreaId = sourceAreaId ?? string.Empty;
            Side = side;
            stations = new ReadOnlyCollection<CityMountainRidgeStation>(
                new List<CityMountainRidgeStation>(sourceStations));
            IsSouthWestJoin = isSouthWestJoin;
            XZBounds = CalculateBounds(stations);
        }

        public string StableId { get; }

        /// <summary>
        /// Area whose continuous top supplied BaseY. Empty only for the
        /// diagonal south-west join, whose endpoint samples are interpolated.
        /// </summary>
        public string SourceAreaId { get; }

        public CityMountainBoundarySide Side { get; }
        public IReadOnlyList<CityMountainRidgeStation> Stations => stations;
        public bool IsSouthWestJoin { get; }

        /// <summary>
        /// Complete XZ envelope including every station's outer foot.
        /// </summary>
        public Rect XZBounds { get; }

        public Vector2 StartXZ => stations.Count == 0
            ? Vector2.zero
            : stations[0].WorldXZ;

        public Vector2 EndXZ => stations.Count == 0
            ? Vector2.zero
            : stations[stations.Count - 1].WorldXZ;

        private static Rect CalculateBounds(
            IReadOnlyList<CityMountainRidgeStation> source)
        {
            if (source.Count == 0)
            {
                return default;
            }

            Vector2 firstOuter = ToXZ(source[0].OuterFoot);
            float xMin = Mathf.Min(source[0].WorldXZ.x, firstOuter.x);
            float xMax = Mathf.Max(source[0].WorldXZ.x, firstOuter.x);
            float zMin = Mathf.Min(source[0].WorldXZ.y, firstOuter.y);
            float zMax = Mathf.Max(source[0].WorldXZ.y, firstOuter.y);
            for (int index = 1; index < source.Count; index++)
            {
                CityMountainRidgeStation station = source[index];
                Vector2 outer = ToXZ(station.OuterFoot);
                xMin = Mathf.Min(xMin, station.WorldXZ.x, outer.x);
                xMax = Mathf.Max(xMax, station.WorldXZ.x, outer.x);
                zMin = Mathf.Min(zMin, station.WorldXZ.y, outer.y);
                zMax = Mathf.Max(zMax, station.WorldXZ.y, outer.y);
            }

            return Rect.MinMaxRect(xMin, zMin, xMax, zMax);
        }

        private static Vector2 ToXZ(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }
    }

    /// <summary>
    /// The authoritative ground skin for the otherwise omitted south-west
    /// blueprint corner. Its perimeter follows the west yard, the road-node
    /// corner, the south yard and every toe station of the diagonal join.
    /// </summary>
    public sealed class CityMountainCornerClosureDescriptor :
        IEquatable<CityMountainCornerClosureDescriptor>
    {
        public const string SouthWestStableId =
            "mountain-south-west-corner-apron";

        public const float MaximumNaturalGroundSlopeDegrees = 18f;

        private readonly ReadOnlyCollection<Vector3> westBoundarySamples;
        private readonly ReadOnlyCollection<Vector3> southBoundarySamples;
        private readonly ReadOnlyCollection<Vector3> vertices;
        private readonly ReadOnlyCollection<int> surfaceTriangleIndices;
        private readonly ReadOnlyCollection<int> supportTriangleIndices;
        private readonly ReadOnlyCollection<int> triangleIndices;

        internal CityMountainCornerClosureDescriptor(
            string stableId,
            Vector2Int voidCell,
            string joinStableId,
            string westAreaId,
            string southAreaId,
            IList<Vector3> sourceWestBoundarySamples,
            Vector3 roadCorner,
            IList<Vector3> sourceSouthBoundarySamples,
            Vector3 joinMiddleToe)
        {
            if (sourceWestBoundarySamples == null ||
                sourceWestBoundarySamples.Count < 2)
            {
                throw new ArgumentException(
                    "A mountain corner closure requires a sampled west " +
                    "boundary.",
                    nameof(sourceWestBoundarySamples));
            }

            if (sourceSouthBoundarySamples == null ||
                sourceSouthBoundarySamples.Count < 2)
            {
                throw new ArgumentException(
                    "A mountain corner closure requires a sampled south " +
                    "boundary.",
                    nameof(sourceSouthBoundarySamples));
            }

            StableId = stableId ?? string.Empty;
            VoidCell = voidCell;
            JoinStableId = joinStableId ?? string.Empty;
            WestAreaId = westAreaId ?? string.Empty;
            SouthAreaId = southAreaId ?? string.Empty;
            westBoundarySamples = new ReadOnlyCollection<Vector3>(
                new List<Vector3>(sourceWestBoundarySamples));
            southBoundarySamples = new ReadOnlyCollection<Vector3>(
                new List<Vector3>(sourceSouthBoundarySamples));

            var vertexSource = new List<Vector3>(
                westBoundarySamples.Count + southBoundarySamples.Count + 2);
            vertexSource.AddRange(westBoundarySamples);
            RoadCornerVertexIndex = vertexSource.Count;
            vertexSource.Add(roadCorner);
            SouthBoundaryStartVertexIndex = vertexSource.Count;
            vertexSource.AddRange(southBoundarySamples);
            JoinMiddleToeVertexIndex = vertexSource.Count;
            vertexSource.Add(joinMiddleToe);
            var surfaceTriangles = new List<int>(18 * 3);
            var supportTriangles = new List<int>();
            BuildNaturalGroundTriangles(
                westBoundarySamples.Count,
                southBoundarySamples.Count,
                RoadCornerVertexIndex,
                SouthBoundaryStartVertexIndex,
                JoinMiddleToeVertexIndex,
                surfaceTriangles);
            vertices = new ReadOnlyCollection<Vector3>(vertexSource);
            surfaceTriangleIndices = new ReadOnlyCollection<int>(
                surfaceTriangles);
            supportTriangleIndices = new ReadOnlyCollection<int>(
                supportTriangles);
            var triangles = new List<int>(
                surfaceTriangles.Count + supportTriangles.Count);
            triangles.AddRange(surfaceTriangles);
            triangles.AddRange(supportTriangles);
            triangleIndices = new ReadOnlyCollection<int>(triangles);
            XZBounds = CalculateBounds(vertices);
            ProjectedArea = CalculateProjectedArea(
                westBoundarySamples,
                roadCorner,
                southBoundarySamples,
                joinMiddleToe);

            MaximumSurfaceSlopeDegrees = CalculateMaximumSurfaceSlope(
                vertices,
                surfaceTriangleIndices);
            NaturalGroundSlopeDegrees = Mathf.Max(
                CalculateTriangleSlope(
                    vertices[0],
                    roadCorner,
                    joinMiddleToe),
                CalculateTriangleSlope(
                    joinMiddleToe,
                    roadCorner,
                    southBoundarySamples[
                        southBoundarySamples.Count - 1]));
        }

        public string StableId { get; }
        public Vector2Int VoidCell { get; }
        public string JoinStableId { get; }
        public string WestAreaId { get; }
        public string SouthAreaId { get; }
        public IReadOnlyList<Vector3> WestBoundarySamples =>
            westBoundarySamples;
        public IReadOnlyList<Vector3> SouthBoundarySamples =>
            southBoundarySamples;
        public IReadOnlyList<Vector3> Vertices => vertices;
        public IReadOnlyList<int> SurfaceTriangleIndices =>
            surfaceTriangleIndices;
        public IReadOnlyList<int> SupportTriangleIndices =>
            supportTriangleIndices;
        public IReadOnlyList<int> TriangleIndices => triangleIndices;
        public Rect XZBounds { get; }
        public float ProjectedArea { get; }
        public float MaximumSurfaceSlopeDegrees { get; }
        public float NaturalGroundSlopeDegrees { get; }
        public int RoadCornerVertexIndex { get; }
        public int SouthBoundaryStartVertexIndex { get; }
        public int JoinMiddleToeVertexIndex { get; }
        public int SouthToeVertexIndex =>
            SouthBoundaryStartVertexIndex + southBoundarySamples.Count - 1;

        public Vector3 WestToe => westBoundarySamples[0];
        public Vector3 WestGround =>
            westBoundarySamples[westBoundarySamples.Count - 1];
        public Vector3 RoadCorner => vertices[RoadCornerVertexIndex];
        public Vector3 SouthGround => southBoundarySamples[0];
        public Vector3 SouthToe =>
            southBoundarySamples[southBoundarySamples.Count - 1];
        public Vector3 JoinMiddleToe => vertices[JoinMiddleToeVertexIndex];

        public bool Equals(CityMountainCornerClosureDescriptor other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (!string.Equals(
                    StableId,
                    other.StableId,
                    StringComparison.Ordinal) ||
                VoidCell != other.VoidCell ||
                !string.Equals(
                    JoinStableId,
                    other.JoinStableId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    WestAreaId,
                    other.WestAreaId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    SouthAreaId,
                    other.SouthAreaId,
                    StringComparison.Ordinal) ||
                vertices.Count != other.vertices.Count ||
                triangleIndices.Count != other.triangleIndices.Count)
            {
                return false;
            }

            for (int index = 0; index < vertices.Count; index++)
            {
                if (!vertices[index].Equals(other.vertices[index]))
                {
                    return false;
                }
            }

            for (int index = 0; index < triangleIndices.Count; index++)
            {
                if (triangleIndices[index] != other.triangleIndices[index])
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return obj is CityMountainCornerClosureDescriptor other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(
                    StableId ?? string.Empty);
                hash = (hash * 397) ^ VoidCell.GetHashCode();
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(
                    JoinStableId ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(
                    WestAreaId ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(
                    SouthAreaId ?? string.Empty);
                for (int index = 0; index < vertices.Count; index++)
                {
                    hash = (hash * 397) ^ vertices[index].GetHashCode();
                }

                for (int index = 0; index < triangleIndices.Count; index++)
                {
                    hash = (hash * 397) ^ triangleIndices[index];
                }

                return hash;
            }
        }

        private static Rect CalculateBounds(IReadOnlyList<Vector3> source)
        {
            float xMin = source[0].x;
            float xMax = source[0].x;
            float zMin = source[0].z;
            float zMax = source[0].z;
            for (int index = 1; index < source.Count; index++)
            {
                Vector3 vertex = source[index];
                xMin = Mathf.Min(xMin, vertex.x);
                xMax = Mathf.Max(xMax, vertex.x);
                zMin = Mathf.Min(zMin, vertex.z);
                zMax = Mathf.Max(zMax, vertex.z);
            }

            return Rect.MinMaxRect(xMin, zMin, xMax, zMax);
        }

        private static float CalculateProjectedArea(
            IReadOnlyList<Vector3> west,
            Vector3 roadCorner,
            IReadOnlyList<Vector3> south,
            Vector3 joinMiddleToe)
        {
            var source = new List<Vector3>(
                west.Count + south.Count + 2);
            source.AddRange(west);
            source.Add(roadCorner);
            source.AddRange(south);
            source.Add(joinMiddleToe);
            float twiceArea = 0f;
            for (int index = 0; index < source.Count; index++)
            {
                Vector3 current = source[index];
                Vector3 next = source[(index + 1) % source.Count];
                twiceArea += current.x * next.z - next.x * current.z;
            }

            return Mathf.Abs(twiceArea) * 0.5f;
        }

        private static float CalculateMaximumSurfaceSlope(
            IReadOnlyList<Vector3> sourceVertices,
            IReadOnlyList<int> sourceTriangles)
        {
            float maximum = 0f;
            for (int index = 0; index < sourceTriangles.Count; index += 3)
            {
                Vector3 first = sourceVertices[sourceTriangles[index]];
                Vector3 second = sourceVertices[sourceTriangles[index + 1]];
                Vector3 third = sourceVertices[sourceTriangles[index + 2]];
                maximum = Mathf.Max(
                    maximum,
                    CalculateTriangleSlope(first, second, third));
            }

            return maximum;
        }

        private static float CalculateTriangleSlope(
            Vector3 first,
            Vector3 second,
            Vector3 third)
        {
            Vector3 normal = Vector3.Cross(
                second - first,
                third - first).normalized;
            return Vector3.Angle(normal, Vector3.up);
        }

        private static void BuildNaturalGroundTriangles(
            int westSampleCount,
            int southSampleCount,
            int roadIndex,
            int southStartIndex,
            int middleToeIndex,
            ICollection<int> destination)
        {
            for (int index = 0; index < westSampleCount - 1; index++)
            {
                AddTriangle(destination, index, index + 1, roadIndex);
            }

            AddTriangle(destination, 0, roadIndex, middleToeIndex);
            AddTriangle(
                destination,
                middleToeIndex,
                roadIndex,
                southStartIndex + southSampleCount - 1);

            for (int index = 0; index < southSampleCount - 1; index++)
            {
                AddTriangle(
                    destination,
                    roadIndex,
                    southStartIndex + index,
                    southStartIndex + index + 1);
            }
        }

        private static void AddTriangle(
            ICollection<int> destination,
            int first,
            int second,
            int third)
        {
            destination.Add(first);
            destination.Add(second);
            destination.Add(third);
        }
    }

    public readonly struct CityMountainRiverNotchDescriptor :
        IEquatable<CityMountainRiverNotchDescriptor>
    {
        public CityMountainRiverNotchDescriptor(
            string stableId,
            Rect approachBounds,
            Rect waterApproachBounds,
            Rect mouthBounds,
            Rect throatWaterBounds,
            Rect westBankBounds,
            Rect eastBankBounds,
            Rect westPromenadeBounds,
            Rect eastPromenadeBounds,
            Vector3 axis,
            float baseY,
            float westCityBankY,
            float westMouthBankY,
            float eastCityBankY,
            float eastMouthBankY,
            float openingHeight,
            float throatDepth,
            float westPeakY,
            float eastPeakY)
        {
            StableId = stableId ?? string.Empty;
            ApproachBounds = approachBounds;
            WaterApproachBounds = waterApproachBounds;
            MouthBounds = mouthBounds;
            ThroatWaterBounds = throatWaterBounds;
            WestBankBounds = westBankBounds;
            EastBankBounds = eastBankBounds;
            WestPromenadeBounds = westPromenadeBounds;
            EastPromenadeBounds = eastPromenadeBounds;
            Axis = axis;
            BaseY = baseY;
            WestCityBankY = westCityBankY;
            WestMouthBankY = westMouthBankY;
            EastCityBankY = eastCityBankY;
            EastMouthBankY = eastMouthBankY;
            OpeningHeight = openingHeight;
            ThroatDepth = throatDepth;
            WestPeakY = westPeakY;
            EastPeakY = eastPeakY;
        }

        public string StableId { get; }
        public CityMountainBoundarySide Side =>
            CityMountainBoundarySide.South;
        /// <summary>
        /// The complete visible forefield between the authored city river and
        /// the mountain toe. It is deliberately wider than the water because
        /// it also owns both bank approaches and their natural shoulders.
        /// </summary>
        public Rect ApproachBounds { get; }

        public Rect WaterApproachBounds { get; }
        public Rect MouthBounds { get; }
        public Rect ThroatWaterBounds { get; }
        public Rect WestBankBounds { get; }
        public Rect EastBankBounds { get; }
        public Rect WestPromenadeBounds { get; }
        public Rect EastPromenadeBounds { get; }

        /// <summary>Direction from the city into the mountain.</summary>
        public Vector3 Axis { get; }

        public float BaseY { get; }
        public float WestCityBankY { get; }
        public float WestMouthBankY { get; }
        public float EastCityBankY { get; }
        public float EastMouthBankY { get; }
        public float OpeningHeight { get; }
        public float ThroatDepth { get; }
        public float WestPeakY { get; }
        public float EastPeakY { get; }

        // Compatibility vocabulary for older consumers while the descriptor
        // now represents a closed cave terminus rather than an open notch.
        public Rect OpeningBounds => ApproachBounds;
        public Vector3 ChannelAxis => -Axis;
        public float ClearWidth => ApproachBounds.width;

        public bool Equals(CityMountainRiverNotchDescriptor other)
        {
            return string.Equals(
                       StableId,
                       other.StableId,
                       StringComparison.Ordinal) &&
                   ApproachBounds.Equals(other.ApproachBounds) &&
                   WaterApproachBounds.Equals(other.WaterApproachBounds) &&
                   MouthBounds.Equals(other.MouthBounds) &&
                   ThroatWaterBounds.Equals(other.ThroatWaterBounds) &&
                   WestBankBounds.Equals(other.WestBankBounds) &&
                   EastBankBounds.Equals(other.EastBankBounds) &&
                   WestPromenadeBounds.Equals(other.WestPromenadeBounds) &&
                   EastPromenadeBounds.Equals(other.EastPromenadeBounds) &&
                   Axis.Equals(other.Axis) &&
                   BaseY.Equals(other.BaseY) &&
                   WestCityBankY.Equals(other.WestCityBankY) &&
                   WestMouthBankY.Equals(other.WestMouthBankY) &&
                   EastCityBankY.Equals(other.EastCityBankY) &&
                   EastMouthBankY.Equals(other.EastMouthBankY) &&
                   OpeningHeight.Equals(other.OpeningHeight) &&
                   ThroatDepth.Equals(other.ThroatDepth) &&
                   WestPeakY.Equals(other.WestPeakY) &&
                   EastPeakY.Equals(other.EastPeakY);
        }

        public override bool Equals(object obj)
        {
            return obj is CityMountainRiverNotchDescriptor other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(
                    StableId ?? string.Empty);
                hash = (hash * 397) ^ ApproachBounds.GetHashCode();
                hash = (hash * 397) ^ WaterApproachBounds.GetHashCode();
                hash = (hash * 397) ^ MouthBounds.GetHashCode();
                hash = (hash * 397) ^ ThroatWaterBounds.GetHashCode();
                hash = (hash * 397) ^ WestBankBounds.GetHashCode();
                hash = (hash * 397) ^ EastBankBounds.GetHashCode();
                hash = (hash * 397) ^ WestPromenadeBounds.GetHashCode();
                hash = (hash * 397) ^ EastPromenadeBounds.GetHashCode();
                hash = (hash * 397) ^ Axis.GetHashCode();
                hash = (hash * 397) ^ BaseY.GetHashCode();
                hash = (hash * 397) ^ WestCityBankY.GetHashCode();
                hash = (hash * 397) ^ WestMouthBankY.GetHashCode();
                hash = (hash * 397) ^ EastCityBankY.GetHashCode();
                hash = (hash * 397) ^ EastMouthBankY.GetHashCode();
                hash = (hash * 397) ^ OpeningHeight.GetHashCode();
                hash = (hash * 397) ^ ThroatDepth.GetHashCode();
                hash = (hash * 397) ^ WestPeakY.GetHashCode();
                return (hash * 397) ^ EastPeakY.GetHashCode();
            }
        }
    }

    /// <summary>
    /// One immutable centre-line chord of the visible mountain tunnel. The
    /// first chords are physical; later chords are presentation only because
    /// the unavailable travel decision is made before the bend.
    /// </summary>
    public readonly struct CityMountainTunnelSegmentDescriptor :
        IEquatable<CityMountainTunnelSegmentDescriptor>
    {
        public CityMountainTunnelSegmentDescriptor(
            string stableId,
            float startDistance,
            float endDistance,
            Vector3 start,
            Vector3 end,
            bool hasCollision)
        {
            StableId = stableId ?? string.Empty;
            StartDistance = startDistance;
            EndDistance = endDistance;
            Start = start;
            End = end;
            HasCollision = hasCollision;
        }

        public string StableId { get; }
        public float StartDistance { get; }
        public float EndDistance { get; }
        public Vector3 Start { get; }
        public Vector3 End { get; }
        public bool HasCollision { get; }
        public float Length => EndDistance - StartDistance;
        public Vector3 Center => (Start + End) * 0.5f;
        public Vector3 Forward => (End - Start).normalized;

        public bool Equals(CityMountainTunnelSegmentDescriptor other)
        {
            return string.Equals(
                       StableId,
                       other.StableId,
                       StringComparison.Ordinal) &&
                   StartDistance.Equals(other.StartDistance) &&
                   EndDistance.Equals(other.EndDistance) &&
                   Start.Equals(other.Start) &&
                   End.Equals(other.End) &&
                   HasCollision == other.HasCollision;
        }

        public override bool Equals(object obj)
        {
            return obj is CityMountainTunnelSegmentDescriptor other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(
                    StableId ?? string.Empty);
                hash = (hash * 397) ^ StartDistance.GetHashCode();
                hash = (hash * 397) ^ EndDistance.GetHashCode();
                hash = (hash * 397) ^ Start.GetHashCode();
                hash = (hash * 397) ^ End.GetHashCode();
                return (hash * 397) ^ HasCollision.GetHashCode();
            }
        }
    }

    public readonly struct CityMountainTunnelPathSample
    {
        public CityMountainTunnelPathSample(
            Vector3 position,
            Vector3 forward,
            float distance)
        {
            Position = position;
            Forward = forward;
            Distance = distance;
        }

        public Vector3 Position { get; }
        public Vector3 Forward { get; }
        public float Distance { get; }
    }

    public readonly struct CityMountainTunnelDescriptor :
        IEquatable<CityMountainTunnelDescriptor>
    {
        private readonly ReadOnlyCollection<
            CityMountainTunnelSegmentDescriptor> segments;

        public CityMountainTunnelDescriptor(
            string stableId,
            string targetAccessId,
            string areaId,
            Vector3 portalGroundCenter,
            Vector3 axis,
            Rect portalBounds,
            Rect approachBounds,
            float openingWidth,
            float openingHeight,
            float visualDepth,
            float walkableDepth,
            float decisionDistance,
            float returnDistance,
            float mapDisplayDepth,
            bool hasPhysicalGate,
            bool travelAvailable,
            IList<CityMountainTunnelSegmentDescriptor> sourceSegments)
        {
            StableId = stableId ?? string.Empty;
            TargetAccessId = targetAccessId ?? string.Empty;
            AreaId = areaId ?? string.Empty;
            PortalGroundCenter = portalGroundCenter;
            Axis = axis;
            PortalBounds = portalBounds;
            ApproachBounds = approachBounds;
            OpeningWidth = openingWidth;
            OpeningHeight = openingHeight;
            VisualDepth = visualDepth;
            WalkableDepth = walkableDepth;
            DecisionDistance = decisionDistance;
            ReturnDistance = returnDistance;
            MapDisplayDepth = mapDisplayDepth;
            HasPhysicalGate = hasPhysicalGate;
            TravelAvailable = travelAvailable;
            segments = new ReadOnlyCollection<
                CityMountainTunnelSegmentDescriptor>(
                new List<CityMountainTunnelSegmentDescriptor>(
                    sourceSegments ??
                    Array.Empty<CityMountainTunnelSegmentDescriptor>()));
        }

        public string StableId { get; }
        public string TargetAccessId { get; }
        public string AreaId { get; }
        public Vector3 PortalGroundCenter { get; }

        /// <summary>Direction from the city into the mountain.</summary>
        public Vector3 Axis { get; }

        public Rect PortalBounds { get; }
        public Rect ApproachBounds { get; }
        public float OpeningWidth { get; }
        public float OpeningHeight { get; }
        public float VisualDepth { get; }
        public float WalkableDepth { get; }
        public float DecisionDistance { get; }
        public float ReturnDistance { get; }
        public float MapDisplayDepth { get; }
        public bool HasPhysicalGate { get; }
        public bool TravelAvailable { get; }
        public IReadOnlyList<CityMountainTunnelSegmentDescriptor> Segments =>
            segments ??
            (IReadOnlyList<CityMountainTunnelSegmentDescriptor>)
            Array.Empty<CityMountainTunnelSegmentDescriptor>();

        /// <summary>
        /// Compatibility vocabulary for consumers that only need the visible
        /// lining length. It does not imply a short or sealed throat.
        /// </summary>
        public float ThroatDepth => VisualDepth;

        /// <summary>
        /// Samples the ground-centre path at an arc distance from the portal.
        /// Fixture, blocker and later transition plans share this path rather
        /// than reproducing the bend formula.
        /// </summary>
        public CityMountainTunnelPathSample SamplePath(float distance)
        {
            float clamped = Mathf.Clamp(distance, 0f, VisualDepth);
            IReadOnlyList<CityMountainTunnelSegmentDescriptor> path =
                Segments;
            for (int index = 0; index < path.Count; index++)
            {
                CityMountainTunnelSegmentDescriptor segment = path[index];
                if (clamped > segment.EndDistance &&
                    index < path.Count - 1)
                {
                    continue;
                }

                float t = segment.Length > 0.0001f
                    ? Mathf.Clamp01(
                        (clamped - segment.StartDistance) / segment.Length)
                    : 0f;
                return new CityMountainTunnelPathSample(
                    Vector3.Lerp(segment.Start, segment.End, t),
                    segment.Forward,
                    clamped);
            }

            Vector3 forward = Axis.sqrMagnitude > 0.0001f
                ? Axis.normalized
                : Vector3.back;
            return new CityMountainTunnelPathSample(
                PortalGroundCenter + forward * clamped,
                forward,
                clamped);
        }

        public bool Equals(CityMountainTunnelDescriptor other)
        {
            if (!string.Equals(
                    StableId,
                    other.StableId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    TargetAccessId,
                    other.TargetAccessId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    AreaId,
                    other.AreaId,
                    StringComparison.Ordinal) ||
                !PortalGroundCenter.Equals(other.PortalGroundCenter) ||
                !Axis.Equals(other.Axis) ||
                !PortalBounds.Equals(other.PortalBounds) ||
                !ApproachBounds.Equals(other.ApproachBounds) ||
                !OpeningWidth.Equals(other.OpeningWidth) ||
                !OpeningHeight.Equals(other.OpeningHeight) ||
                !VisualDepth.Equals(other.VisualDepth) ||
                !WalkableDepth.Equals(other.WalkableDepth) ||
                !DecisionDistance.Equals(other.DecisionDistance) ||
                !ReturnDistance.Equals(other.ReturnDistance) ||
                !MapDisplayDepth.Equals(other.MapDisplayDepth) ||
                HasPhysicalGate != other.HasPhysicalGate ||
                TravelAvailable != other.TravelAvailable ||
                Segments.Count != other.Segments.Count)
            {
                return false;
            }

            for (int index = 0; index < Segments.Count; index++)
            {
                if (!Segments[index].Equals(other.Segments[index]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return obj is CityMountainTunnelDescriptor other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(
                    StableId ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(
                    TargetAccessId ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(
                    AreaId ?? string.Empty);
                hash = (hash * 397) ^ PortalGroundCenter.GetHashCode();
                hash = (hash * 397) ^ Axis.GetHashCode();
                hash = (hash * 397) ^ PortalBounds.GetHashCode();
                hash = (hash * 397) ^ ApproachBounds.GetHashCode();
                hash = (hash * 397) ^ OpeningWidth.GetHashCode();
                hash = (hash * 397) ^ OpeningHeight.GetHashCode();
                hash = (hash * 397) ^ VisualDepth.GetHashCode();
                hash = (hash * 397) ^ WalkableDepth.GetHashCode();
                hash = (hash * 397) ^ DecisionDistance.GetHashCode();
                hash = (hash * 397) ^ ReturnDistance.GetHashCode();
                hash = (hash * 397) ^ MapDisplayDepth.GetHashCode();
                hash = (hash * 397) ^ HasPhysicalGate.GetHashCode();
                hash = (hash * 397) ^ TravelAvailable.GetHashCode();
                for (int index = 0; index < Segments.Count; index++)
                {
                    hash = (hash * 397) ^ Segments[index].GetHashCode();
                }

                return hash;
            }
        }
    }

    public sealed class CityMountainBoundaryPlan
    {
        private static readonly CityMountainBoundaryPlan EmptyPlan =
            new CityMountainBoundaryPlan(
                null,
                Array.Empty<CityMountainRidgeDescriptor>(),
                false,
                null,
                false,
                default,
                false,
                default);

        private readonly ReadOnlyCollection<CityMountainRidgeDescriptor>
            ridges;

        internal CityMountainBoundaryPlan(
            CityMountainBoundaryDefinition definition,
            IList<CityMountainRidgeDescriptor> ridgeSource,
            bool hasSouthWestCornerClosure,
            CityMountainCornerClosureDescriptor southWestCornerClosure,
            bool hasRiverNotch,
            CityMountainRiverNotchDescriptor riverNotch,
            bool hasTunnel,
            CityMountainTunnelDescriptor tunnel)
        {
            Definition = definition;
            ridges = new ReadOnlyCollection<CityMountainRidgeDescriptor>(
                new List<CityMountainRidgeDescriptor>(ridgeSource));
            HasSouthWestCornerClosure = hasSouthWestCornerClosure;
            SouthWestCornerClosure = southWestCornerClosure;
            HasRiverNotch = hasRiverNotch;
            RiverNotch = riverNotch;
            HasTunnel = hasTunnel;
            Tunnel = tunnel;
        }

        public static CityMountainBoundaryPlan Empty => EmptyPlan;
        public CityMountainBoundaryDefinition Definition { get; }
        public bool IsEnabled => Definition != null;
        public IReadOnlyList<CityMountainRidgeDescriptor> Ridges => ridges;
        public int RidgeCount => ridges.Count;
        public bool HasSouthWestCornerClosure { get; }
        public CityMountainCornerClosureDescriptor SouthWestCornerClosure
        {
            get;
        }
        public bool HasRiverNotch { get; }
        public CityMountainRiverNotchDescriptor RiverNotch { get; }
        public bool HasRiverCave => HasRiverNotch;
        public CityMountainRiverNotchDescriptor RiverCave => RiverNotch;
        public bool HasTunnel { get; }
        public CityMountainTunnelDescriptor Tunnel { get; }

        public int GetRidgeCount(CityMountainBoundarySide side)
        {
            int count = 0;
            for (int index = 0; index < ridges.Count; index++)
            {
                if (ridges[index].Side == side)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
