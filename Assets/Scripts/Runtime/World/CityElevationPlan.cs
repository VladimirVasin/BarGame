using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    [Flags]
    public enum CityTraversalMobility
    {
        None = 0,
        Player = 1,
        Pedestrian = 2,
        Vehicle = 4,
        Bus = 8,
        All = Player | Pedestrian | Vehicle | Bus
    }

    public enum CityElevationTransitionKind
    {
        Level = 0,
        VehicleGrade = 1,
        PedestrianStair = 2,
        ProtectedDrop = 3
    }

    public enum CityElevationStairSide
    {
        None = 0,
        Left = -1,
        Right = 1
    }

    public enum CitySurfaceRole
    {
        GroundDatum = 0,
        GroundTop = 1,
        RoadDatum = 2,
        RoadTop = 3,
        SidewalkTop = 4
    }

    public sealed class DistrictElevationProfile
    {
        internal DistrictElevationProfile(
            CityDistrictKind district,
            float minimumElevation,
            float maximumElevation,
            int preferredStairConnections)
        {
            District = district;
            MinimumElevation = minimumElevation;
            MaximumElevation = maximumElevation;
            PreferredStairConnections = preferredStairConnections;
        }

        public CityDistrictKind District { get; }
        public float MinimumElevation { get; }
        public float MaximumElevation { get; }
        public int PreferredStairConnections { get; }
    }

    public readonly struct CityElevationTransitionDescriptor
    {
        internal CityElevationTransitionDescriptor(
            RoadEdge edge,
            CityPathKind pathKind,
            CityElevationTransitionKind kind,
            CityTraversalMobility mobility,
            float startElevation,
            float endElevation,
            float horizontalRun,
            float gradePercent)
        {
            Edge = edge;
            PathKind = pathKind;
            Kind = kind;
            Mobility = mobility;
            StartElevation = startElevation;
            EndElevation = endElevation;
            HorizontalRun = horizontalRun;
            GradePercent = gradePercent;
        }

        public RoadEdge Edge { get; }
        public CityPathKind PathKind { get; }
        public CityElevationTransitionKind Kind { get; }
        public CityTraversalMobility Mobility { get; }
        public float StartElevation { get; }
        public float EndElevation { get; }
        public float HorizontalRun { get; }
        public float GradePercent { get; }
        public float ElevationDelta => EndElevation - StartElevation;
    }

    public readonly struct CityElevationStairDescriptor
    {
        internal CityElevationStairDescriptor(
            string id,
            CityDistrictKind district,
            RoadEdge edge,
            Vector2Int lowerNode,
            Vector2Int upperNode,
            CityElevationStairSide side,
            int stepCount,
            float stepRise,
            float treadDepth,
            float width,
            float landingLength)
        {
            Id = id ?? string.Empty;
            District = district;
            Edge = edge;
            LowerNode = lowerNode;
            UpperNode = upperNode;
            Side = side;
            StepCount = stepCount;
            StepRise = stepRise;
            TreadDepth = treadDepth;
            Width = width;
            LandingLength = landingLength;
        }

        public string Id { get; }
        public CityDistrictKind District { get; }
        public RoadEdge Edge { get; }
        public Vector2Int LowerNode { get; }
        public Vector2Int UpperNode { get; }
        public CityElevationStairSide Side { get; }
        public int StepCount { get; }
        public float StepRise { get; }
        public float TreadDepth { get; }
        public float Width { get; }
        public float LandingLength { get; }
        public float TotalRise => StepCount * StepRise;
        public float RunLength => StepCount * TreadDepth;
    }

    public sealed class CityElevationPlan
    {
        public const float GroundTopOffset = -0.08f;
        public const float MaximumBusGradePercent = 6f;
        public const float MaximumPedestrianGradePercent = 8.3f;

        private readonly ReadOnlyDictionary<Vector2Int, float>
            nodeElevations;
        private readonly ReadOnlyDictionary<Vector2Int, float>
            cellElevations;
        private readonly ReadOnlyDictionary<RoadEdge,
            CityElevationTransitionDescriptor> transitions;
        private readonly ReadOnlyDictionary<CityDistrictKind,
            DistrictElevationProfile> profiles;
        private readonly HashSet<Vector2Int> cellSet;
        private readonly List<RoadEdge> orderedEdges;
        // Lazily built cell index over orderedEdges for TrySampleRoad. Each
        // edge is filed under every node-spacing cell that its segment,
        // widened by halfRoad plus RoadIndexMargin, overlaps, so a point
        // within halfRoad of an edge always finds that edge under the point's
        // own cell. The per-cell lists keep orderedEdges order and the scan
        // over them is the original comparison, so the winner and its
        // first-wins tie-break are unchanged; a point outside the indexed
        // cells (or a NaN) takes the original full scan.
        private const float RoadIndexMargin = 1f;
        private const int RoadIndexCellLimit = 1 << 22;
        private int[] roadCellStarts;
        private int[] roadCellEntries;
        private Vector2Int roadCellMin;
        private Vector2Int roadCellSize;
        private bool roadIndexBuilt;

        internal CityElevationPlan(
            string blueprintId,
            int seed,
            Vector3 worldOrigin,
            Vector2 nodeSpacing,
            float roadWidth,
            bool isElevated,
            IDictionary<Vector2Int, float> sourceNodeElevations,
            IDictionary<Vector2Int, float> sourceCellElevations,
            IDictionary<RoadEdge, CityElevationTransitionDescriptor>
                sourceTransitions,
            IDictionary<CityDistrictKind, DistrictElevationProfile>
                sourceProfiles,
            IList<CityElevationStairDescriptor> signatureStairs)
        {
            BlueprintId = blueprintId ?? string.Empty;
            Seed = seed;
            WorldOrigin = worldOrigin;
            NodeSpacing = nodeSpacing;
            RoadWidth = roadWidth;
            IsElevated = isElevated;
            nodeElevations = new ReadOnlyDictionary<Vector2Int, float>(
                new Dictionary<Vector2Int, float>(
                    sourceNodeElevations ??
                    throw new ArgumentNullException(
                        nameof(sourceNodeElevations))));
            cellElevations = new ReadOnlyDictionary<Vector2Int, float>(
                new Dictionary<Vector2Int, float>(
                    sourceCellElevations ??
                    throw new ArgumentNullException(
                        nameof(sourceCellElevations))));
            transitions = new ReadOnlyDictionary<RoadEdge,
                CityElevationTransitionDescriptor>(
                new Dictionary<RoadEdge,
                    CityElevationTransitionDescriptor>(
                    sourceTransitions ??
                    throw new ArgumentNullException(
                        nameof(sourceTransitions))));
            profiles = new ReadOnlyDictionary<CityDistrictKind,
                DistrictElevationProfile>(
                new Dictionary<CityDistrictKind,
                    DistrictElevationProfile>(
                    sourceProfiles ??
                    throw new ArgumentNullException(
                        nameof(sourceProfiles))));
            SignatureStairs = new ReadOnlyCollection<
                CityElevationStairDescriptor>(
                new List<CityElevationStairDescriptor>(
                    signatureStairs ??
                    throw new ArgumentNullException(
                        nameof(signatureStairs))));
            cellSet = new HashSet<Vector2Int>(cellElevations.Keys);
            orderedEdges = new List<RoadEdge>(transitions.Keys);
            orderedEdges.Sort(RoadEdge.Compare);

            MinimumElevation = float.PositiveInfinity;
            MaximumElevation = float.NegativeInfinity;
            foreach (float elevation in nodeElevations.Values)
            {
                MinimumElevation = Mathf.Min(MinimumElevation, elevation);
                MaximumElevation = Mathf.Max(MaximumElevation, elevation);
            }

            foreach (float elevation in cellElevations.Values)
            {
                MinimumElevation = Mathf.Min(MinimumElevation, elevation);
                MaximumElevation = Mathf.Max(MaximumElevation, elevation);
            }

            if (float.IsPositiveInfinity(MinimumElevation))
            {
                MinimumElevation = 0f;
                MaximumElevation = 0f;
            }
        }

        public string BlueprintId { get; }
        public int Seed { get; }
        public Vector3 WorldOrigin { get; }
        public Vector2 NodeSpacing { get; }
        public float RoadWidth { get; }
        public bool IsElevated { get; }
        public float MinimumElevation { get; private set; }
        public float MaximumElevation { get; private set; }
        public IReadOnlyDictionary<Vector2Int, float> NodeElevations =>
            nodeElevations;
        public IReadOnlyDictionary<Vector2Int, float> CellElevations =>
            cellElevations;
        public IReadOnlyDictionary<RoadEdge,
            CityElevationTransitionDescriptor> Transitions => transitions;
        public IReadOnlyDictionary<CityDistrictKind,
            DistrictElevationProfile> Profiles => profiles;
        public IReadOnlyList<CityElevationStairDescriptor>
            SignatureStairs { get; }

        public float GetNodeElevation(Vector2Int node)
        {
            if (!nodeElevations.TryGetValue(node, out float elevation))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(node),
                    $"Node {node} does not belong to the elevation plan.");
            }

            return elevation;
        }

        public bool TryGetNodeElevation(
            Vector2Int node,
            out float elevation)
        {
            return nodeElevations.TryGetValue(node, out elevation);
        }

        public float GetCellElevation(Vector2Int cell)
        {
            if (!cellElevations.TryGetValue(cell, out float elevation))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cell),
                    $"Cell {cell} does not belong to the elevation plan.");
            }

            return elevation;
        }

        public CityElevationTransitionDescriptor GetTransition(
            RoadEdge edge)
        {
            if (!transitions.TryGetValue(
                    edge,
                    out CityElevationTransitionDescriptor transition))
            {
                throw new ArgumentException(
                    "The edge does not belong to the elevation plan.",
                    nameof(edge));
            }

            return transition;
        }

        public bool TryGetSignatureStair(
            RoadEdge edge,
            out CityElevationStairDescriptor stair)
        {
            for (int index = 0; index < SignatureStairs.Count; index++)
            {
                if (SignatureStairs[index].Edge == edge)
                {
                    stair = SignatureStairs[index];
                    return true;
                }
            }

            stair = default;
            return false;
        }

        public float SampleRoadDatum(RoadEdge edge, float amount)
        {
            amount = Mathf.Clamp01(amount);
            float planarLength = edge.IsHorizontal
                ? NodeSpacing.x
                : NodeSpacing.y;
            float insetAmount = planarLength > 0.001f
                ? Mathf.Clamp01((RoadWidth * 0.5f) / planarLength)
                : 0f;
            amount = Mathf.InverseLerp(
                insetAmount,
                1f - insetAmount,
                amount);
            return Mathf.Lerp(
                GetNodeElevation(edge.A),
                GetNodeElevation(edge.B),
                amount);
        }

        public bool TrySampleSurface(
            Vector2 worldXZ,
            CitySurfaceRole role,
            out float height,
            out Vector3 normal)
        {
            if (role == CitySurfaceRole.RoadDatum ||
                role == CitySurfaceRole.RoadTop ||
                role == CitySurfaceRole.SidewalkTop)
            {
                return TrySampleRoad(
                    worldXZ,
                    role,
                    out height,
                    out normal);
            }

            int cellX = Mathf.FloorToInt(
                (worldXZ.x - WorldOrigin.x) / NodeSpacing.x);
            int cellZ = Mathf.FloorToInt(
                (worldXZ.y - WorldOrigin.z) / NodeSpacing.y);
            var cell = new Vector2Int(cellX, cellZ);
            if (!cellSet.Contains(cell))
            {
                height = 0f;
                normal = Vector3.up;
                return false;
            }

            height = GetCellElevation(cell);
            if (role == CitySurfaceRole.GroundTop)
            {
                height += GroundTopOffset;
            }

            normal = Vector3.up;
            return true;
        }

        private bool TrySampleRoad(
            Vector2 worldXZ,
            CitySurfaceRole role,
            out float height,
            out Vector3 normal)
        {
            bool found = false;
            float bestDistance = float.PositiveInfinity;
            RoadEdge bestEdge = default;
            float bestAmount = 0f;
            float halfRoad = RoadWidth * 0.5f + 0.001f;
            EnsureRoadIndex();
            int firstSlot = 0;
            int lastSlot = orderedEdges.Count;
            int[] slots = null;
            // A NaN or infinite query has no cell: float-to-int of such a
            // value is unspecified by the CLI, so it takes the full scan
            // exactly as before instead of trusting the range check.
            if (roadCellSize.x > 0 &&
                !float.IsNaN(worldXZ.x) && !float.IsInfinity(worldXZ.x) &&
                !float.IsNaN(worldXZ.y) && !float.IsInfinity(worldXZ.y))
            {
                int cellX = RoadIndexCell(worldXZ.x, WorldOrigin.x, NodeSpacing.x)
                    - roadCellMin.x;
                int cellZ = RoadIndexCell(worldXZ.y, WorldOrigin.z, NodeSpacing.y)
                    - roadCellMin.y;
                if ((uint)cellX < (uint)roadCellSize.x &&
                    (uint)cellZ < (uint)roadCellSize.y)
                {
                    int cell = cellZ * roadCellSize.x + cellX;
                    firstSlot = roadCellStarts[cell];
                    lastSlot = roadCellStarts[cell + 1];
                    slots = roadCellEntries;
                }
            }

            for (int slot = firstSlot; slot < lastSlot; slot++)
            {
                RoadEdge edge = orderedEdges[slots == null ? slot : slots[slot]];
                Vector2 start = GetNodeWorldXZ(edge.A);
                Vector2 end = GetNodeWorldXZ(edge.B);
                Vector2 delta = end - start;
                float denominator = delta.sqrMagnitude;
                float amount = denominator > 0.000001f
                    ? Mathf.Clamp01(
                        Vector2.Dot(worldXZ - start, delta) / denominator)
                    : 0f;
                Vector2 projected = start + delta * amount;
                float distance = Vector2.Distance(worldXZ, projected);
                if (distance > halfRoad ||
                    distance >= bestDistance)
                {
                    continue;
                }

                found = true;
                bestDistance = distance;
                bestEdge = edge;
                bestAmount = amount;
            }

            if (!found)
            {
                height = 0f;
                normal = Vector3.up;
                return false;
            }

            height = SampleRoadDatum(bestEdge, bestAmount);
            if (role == CitySurfaceRole.RoadTop)
            {
                height += CityStreetSurfacePlanner.RoadTop;
            }
            else if (role == CitySurfaceRole.SidewalkTop)
            {
                height += CityStreetSurfacePlanner.SidewalkTop;
            }

            Vector3 startWorld = GetNodeWorldPosition(bestEdge.A);
            Vector3 endWorld = GetNodeWorldPosition(bestEdge.B);
            float planarLength = bestEdge.IsHorizontal
                ? NodeSpacing.x
                : NodeSpacing.y;
            float insetAmount = planarLength > 0.001f
                ? Mathf.Clamp01((RoadWidth * 0.5f) / planarLength)
                : 0f;
            Vector3 tangent;
            if (bestAmount <= insetAmount ||
                bestAmount >= 1f - insetAmount)
            {
                tangent = bestEdge.IsHorizontal
                    ? Vector3.right
                    : Vector3.forward;
            }
            else
            {
                Vector3 planar = endWorld - startWorld;
                planar.y = 0f;
                planar = planar.normalized * Mathf.Max(
                    0.001f,
                    planarLength - RoadWidth);
                tangent = (planar + Vector3.up *
                    (endWorld.y - startWorld.y)).normalized;
            }

            Vector3 right = new Vector3(tangent.z, 0f, -tangent.x);
            normal = Vector3.Cross(tangent, right).normalized;
            return true;
        }

        // The same floor the query uses: it is monotone in x, so a cell range
        // taken from the widened segment ends covers every cell a point of
        // that range can land in.
        private static int RoadIndexCell(float value, float origin, float spacing)
        {
            return Mathf.FloorToInt((value - origin) / spacing);
        }

        private void EnsureRoadIndex()
        {
            if (roadIndexBuilt)
            {
                return;
            }

            roadIndexBuilt = true;
            int edgeCount = orderedEdges.Count;
            if (edgeCount == 0 || NodeSpacing.x <= 0f || NodeSpacing.y <= 0f)
            {
                return;
            }

            // The distance test accepts an edge whose closest segment point is
            // within halfRoad of the query, so the query lies inside the
            // segment's bounding box grown by halfRoad; the margin absorbs the
            // rounding of that distance and of the box ends.
            float reach = RoadWidth * 0.5f + 0.001f + RoadIndexMargin;
            var ranges = new int[edgeCount * 4];
            int minX = int.MaxValue;
            int minZ = int.MaxValue;
            int maxX = int.MinValue;
            int maxZ = int.MinValue;
            for (int index = 0; index < edgeCount; index++)
            {
                RoadEdge edge = orderedEdges[index];
                Vector2 start = GetNodeWorldXZ(edge.A);
                Vector2 end = GetNodeWorldXZ(edge.B);
                int x0 = RoadIndexCell(
                    Mathf.Min(start.x, end.x) - reach, WorldOrigin.x, NodeSpacing.x);
                int x1 = RoadIndexCell(
                    Mathf.Max(start.x, end.x) + reach, WorldOrigin.x, NodeSpacing.x);
                int z0 = RoadIndexCell(
                    Mathf.Min(start.y, end.y) - reach, WorldOrigin.z, NodeSpacing.y);
                int z1 = RoadIndexCell(
                    Mathf.Max(start.y, end.y) + reach, WorldOrigin.z, NodeSpacing.y);
                if (x1 < x0 || z1 < z0)
                {
                    // Non-finite node coordinates: leave the plan on the full scan.
                    return;
                }

                ranges[index * 4] = x0;
                ranges[index * 4 + 1] = x1;
                ranges[index * 4 + 2] = z0;
                ranges[index * 4 + 3] = z1;
                minX = Math.Min(minX, x0);
                maxX = Math.Max(maxX, x1);
                minZ = Math.Min(minZ, z0);
                maxZ = Math.Max(maxZ, z1);
            }

            long columns = (long)maxX - minX + 1;
            long rows = (long)maxZ - minZ + 1;
            if (columns <= 0 || rows <= 0 || columns * rows > RoadIndexCellLimit)
            {
                return;
            }

            int cellCount = (int)(columns * rows);
            var starts = new int[cellCount + 1];
            for (int index = 0; index < edgeCount; index++)
            {
                for (int z = ranges[index * 4 + 2]; z <= ranges[index * 4 + 3]; z++)
                {
                    for (int x = ranges[index * 4]; x <= ranges[index * 4 + 1]; x++)
                    {
                        starts[(z - minZ) * (int)columns + (x - minX) + 1]++;
                    }
                }
            }

            for (int cell = 0; cell < cellCount; cell++)
            {
                starts[cell + 1] += starts[cell];
            }

            // Filing in ascending edge order fills each cell's slots in that
            // same order, which is what keeps the tie-break of the scan.
            var entries = new int[starts[cellCount]];
            var fill = new int[cellCount];
            for (int index = 0; index < edgeCount; index++)
            {
                for (int z = ranges[index * 4 + 2]; z <= ranges[index * 4 + 3]; z++)
                {
                    for (int x = ranges[index * 4]; x <= ranges[index * 4 + 1]; x++)
                    {
                        int cell = (z - minZ) * (int)columns + (x - minX);
                        entries[starts[cell] + fill[cell]] = index;
                        fill[cell]++;
                    }
                }
            }

            roadCellStarts = starts;
            roadCellEntries = entries;
            roadCellMin = new Vector2Int(minX, minZ);
            roadCellSize = new Vector2Int((int)columns, (int)rows);
        }

        private Vector2 GetNodeWorldXZ(Vector2Int node)
        {
            return new Vector2(
                WorldOrigin.x + node.x * NodeSpacing.x,
                WorldOrigin.z + node.y * NodeSpacing.y);
        }

        private Vector3 GetNodeWorldPosition(Vector2Int node)
        {
            Vector2 xz = GetNodeWorldXZ(node);
            return new Vector3(
                xz.x,
                GetNodeElevation(node),
                xz.y);
        }
    }
}
