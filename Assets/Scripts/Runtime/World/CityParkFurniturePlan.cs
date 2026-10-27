using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct CityParkFurniturePlacement
    {
        internal CityParkFurniturePlacement(string id, CityMiscKind kind,
            Vector3 position, Quaternion rotation)
        {
            Id = id;
            Kind = kind;
            Position = position;
            Rotation = rotation;
        }

        public string Id { get; }
        public CityMiscKind Kind { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public float ClearanceRadius => Kind == CityMiscKind.ParkBin ? 0.48f : 0.99f;
    }

    /// <summary>
    /// Two bins beside existing benches and one empty notice stand near a
    /// public path. Finite candidates may be skipped rather than narrowing a
    /// route, displacing a tree or intruding on another park feature.
    /// </summary>
    public sealed class CityParkFurniturePlan
    {
        public const int MaximumBinCount = 2;
        public const int MaximumPlacementCount = MaximumBinCount + 1;
        public const float PathSetbackMeters = 0.25f;
        public const float NeighbourClearanceMeters = 0.20f;

        private CityParkFurniturePlan(List<CityParkFurniturePlacement> placements)
        {
            Placements = new ReadOnlyCollection<CityParkFurniturePlacement>(placements);
        }

        public IReadOnlyList<CityParkFurniturePlacement> Placements { get; }

        public static CityParkFurniturePlan Create(CityLayout layout,
            CityDecorationPlan decorations)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (decorations == null) throw new ArgumentNullException(nameof(decorations));
            var placements = new List<CityParkFurniturePlacement>(MaximumPlacementCount);
            if (!layout.Park.IsEnabled) return new CityParkFurniturePlan(placements);
            var roads = new List<Rect>(layout.RoadEdges.Count);
            foreach (RoadEdge edge in layout.RoadEdges) roads.Add(layout.GetRoadRect(edge));
            var proxies = new List<RuntimeOrientedBox>();
            foreach (CityDecorationDescriptor decoration in decorations.Descriptors)
                if (IsGrounded(decoration.AnchorKind))
                    CityStaticCollisionBuilder.AddDecorationProxyBoxes(layout, decoration, proxies);

            foreach (CityParkRegionPlan region in layout.Park.Regions)
            {
                if (placements.Count == MaximumBinCount) break;
                if (TrySelect(layout, decorations, roads, proxies, placements,
                    CityMiscKind.ParkBin, region.Id, region.Center,
                    out CityParkFurniturePlacement bin)) placements.Add(bin);
            }
            Vector3 publicAnchor = CityParkGazeboGeometry.TryFind(decorations,
                out CityDecorationDescriptor gazebo) ? gazebo.Position : layout.Park.Center;
            if (TrySelect(layout, decorations, roads, proxies, placements,
                CityMiscKind.ParkNoticeBoard, null, publicAnchor,
                out CityParkFurniturePlacement board)) placements.Add(board);
            return new CityParkFurniturePlan(placements);
        }

        private static bool TrySelect(CityLayout layout, CityDecorationPlan decorations,
            IReadOnlyList<Rect> roads, IReadOnlyList<RuntimeOrientedBox> proxies,
            IReadOnlyList<CityParkFurniturePlacement> placed, CityMiscKind kind,
            string regionId, Vector3 anchor, out CityParkFurniturePlacement result)
        {
            bool found = false;
            float bestDistance = float.PositiveInfinity;
            result = default;
            bool bin = kind == CityMiscKind.ParkBin;
            foreach (CityParkBenchDescriptor bench in layout.Park.Benches)
            {
                if (regionId != null && !string.Equals(regionId, bench.RegionId,
                    StringComparison.Ordinal)) continue;
                CityParkRegionPlan region = FindRegion(layout.Park, bench.RegionId);
                if (region == null) continue;
                for (int reach = 0; reach < (bin ? 1 : 3); reach++)
                    for (int back = 0; back < 3; back++)
                        for (int side = -1; side <= 1; side += 2)
                        {
                            float lateral = bin ? 1.95f : 3.4f + reach * 0.5f;
                            float setback = bin ? 0.15f + back * 0.3f : 0.90f + back * 0.35f;
                            Vector3 position = bench.Position + bench.Tangent * (side * lateral) -
                                bench.Forward * setback;
                            var candidate = new CityParkFurniturePlacement(
                                bin ? $"park-bin-{bench.RegionId}" : "park-empty-notice-board",
                                kind, position, bench.Rotation);
                            if (!IsClear(layout, decorations, roads, proxies, placed,
                                region.WalkableBounds, candidate) ||
                                !TryGround(layout, candidate, out position)) continue;
                            float distance = FlatSquaredDistance(position, anchor);
                            if (distance >= bestDistance) continue;
                            bestDistance = distance;
                            result = new CityParkFurniturePlacement(candidate.Id, kind,
                                position, bench.Rotation);
                            found = true;
                        }
            }
            return found;
        }

        private static bool IsClear(CityLayout layout, CityDecorationPlan decorations,
            IReadOnlyList<Rect> roads, IReadOnlyList<RuntimeOrientedBox> proxies,
            IReadOnlyList<CityParkFurniturePlacement> placed, Rect region,
            CityParkFurniturePlacement candidate)
        {
            Vector3 point = candidate.Position;
            float radius = candidate.ClearanceRadius;
            float border = radius + NeighbourClearanceMeters;
            if (point.x - border < region.xMin || point.x + border > region.xMax ||
                point.z - border < region.yMin || point.z + border > region.yMax) return false;
            foreach (Rect road in roads)
                if (PointRectDistanceSquared(point, road) < Square(radius + PathSetbackMeters)) return false;
            foreach (Vector3 tree in layout.Park.TreePositions)
                if (FlatSquaredDistance(point, tree) < Square(radius + 0.75f)) return false;
            foreach (CityParkGateDescriptor gate in layout.Park.Gates)
                if (FlatSquaredDistance(point, gate.Center) < Square(radius + gate.Width * 0.5f + 1.25f)) return false;
            foreach (CityParkBenchDescriptor bench in layout.Park.Benches)
            {
                // Preserve the plank ends and the full front approach used by
                // the shared sit interaction, including the selected bench.
                Vector3 local = Quaternion.Inverse(bench.Rotation) * (point - bench.Position);
                Rect seatAndApproach = Rect.MinMaxRect(-1.25f, -0.40f, 1.25f, 1.65f);
                if (PointRectDistanceSquared(local, seatAndApproach) <
                    Square(radius + NeighbourClearanceMeters)) return false;
            }
            foreach (CityDecorationDescriptor decoration in decorations.Descriptors)
                if (IsGrounded(decoration.AnchorKind) && FlatSquaredDistance(point, decoration.Position) <
                    Square(radius + NeighbourClearanceMeters +
                        CityDecorationValidator.ResolveProtectionRadius(decoration.Kind))) return false;
            foreach (RuntimeOrientedBox proxy in proxies)
            {
                Vector3 local = Quaternion.Inverse(proxy.Rotation) * (point - proxy.Center);
                Rect footprint = new Rect(-proxy.Size.x * 0.5f, -proxy.Size.z * 0.5f,
                    proxy.Size.x, proxy.Size.z);
                if (PointRectDistanceSquared(local, footprint) <
                    Square(radius + NeighbourClearanceMeters)) return false;
            }
            foreach (CityParkFurniturePlacement neighbour in placed)
                if (FlatSquaredDistance(point, neighbour.Position) <
                    Square(radius + neighbour.ClearanceRadius + NeighbourClearanceMeters)) return false;
            return true;
        }

        private static bool TryGround(CityLayout layout, CityParkFurniturePlacement placement,
            out Vector3 position)
        {
            position = placement.Position;
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            Vector2 feet = placement.Kind == CityMiscKind.ParkBin
                ? new Vector2(0.39f, 0.10f) : new Vector2(0.915f, 0.12f);
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 sample = position + placement.Rotation *
                        new Vector3(x * feet.x, 0f, z * feet.y);
                    if (!CityTerrainSurfacePlan.TrySampleGroundTop(layout,
                        new Vector2(sample.x, sample.z), out float height, out _)) return false;
                    min = Mathf.Min(min, height);
                    max = Mathf.Max(max, height);
                }
            if (max - min > 0.06f) return false;
            // Shallowly planted feet meet the lawn across its small slope.
            position.y = min - 0.01f;
            return true;
        }

        private static CityParkRegionPlan FindRegion(CityParkPlan park, string id)
        {
            foreach (CityParkRegionPlan region in park.Regions)
                if (string.Equals(region.Id, id, StringComparison.Ordinal)) return region;
            return null;
        }

        private static bool IsGrounded(CityDecorationAnchorKind kind) =>
            kind != CityDecorationAnchorKind.BuildingRoof && kind != CityDecorationAnchorKind.BuildingFacade;

        private static float PointRectDistanceSquared(Vector3 point, Rect rect)
        {
            float x = Mathf.Max(Mathf.Max(rect.xMin - point.x, 0f), point.x - rect.xMax);
            float z = Mathf.Max(Mathf.Max(rect.yMin - point.z, 0f), point.z - rect.yMax);
            return x * x + z * z;
        }

        private static float FlatSquaredDistance(Vector3 a, Vector3 b) =>
            Square(a.x - b.x) + Square(a.z - b.z);
        private static float Square(float value) => value * value;
    }
}
