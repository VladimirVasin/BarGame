using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal static class CityElevationRebaser
    {
        internal static List<BuildingLot> RebaseLots(
            CityElevationPlan elevation,
            IReadOnlyList<BuildingLot> source)
        {
            if (elevation == null)
            {
                throw new ArgumentNullException(nameof(elevation));
            }

            var result = new List<BuildingLot>(source.Count);
            for (int index = 0; index < source.Count; index++)
            {
                BuildingLot lot = source[index];
                float lotDatum = elevation.GetCellElevation(lot.Cell);
                Vector3 center = WithY(lot.Center, lotDatum);
                Vector3 door = WithY(lot.DoorPosition, lotDatum);
                Vector3 roadAnchor = lot.ReturnPosition;
                Vector3 sidewalkAnchor = lot.SidewalkArrivalPosition;
                if (lot.HasRoadFrontage)
                {
                    RoadEdge frontage = RoadEdge.ForCellFrontage(
                        lot.Cell,
                        lot.FrontageDirection);
                    roadAnchor.y = SampleEdgeAtWorldPoint(
                        elevation,
                        frontage,
                        roadAnchor);
                    sidewalkAnchor.y = SampleEdgeAtWorldPoint(
                        elevation,
                        frontage,
                        sidewalkAnchor);
                }
                else
                {
                    roadAnchor.y = lotDatum;
                    sidewalkAnchor.y = lotDatum;
                }

                result.Add(new BuildingLot(
                    lot.Cell,
                    center,
                    lot.Size,
                    lot.Height,
                    lot.Color,
                    lot.AreaId,
                    lot.District,
                    lot.LandUse,
                    lot.IsBar,
                    lot.IsPlayerHome,
                    lot.IsSupermarket,
                    lot.BarId,
                    lot.BarActivity,
                    lot.FrontageDirection,
                    door,
                    roadAnchor,
                    sidewalkAnchor));
            }

            return result;
        }

        internal static CityParkPlan RebasePark(
            CityElevationPlan elevation,
            CityParkPlan source)
        {
            if (source == null || !source.IsEnabled)
            {
                return source;
            }

            float fallbackDatum = AverageCellElevation(
                elevation,
                source.Cells);
            float centerDatum = SampleRoadOrGround(
                elevation,
                source.Center,
                fallbackDatum);
            Vector3 center = WithY(source.Center, centerDatum);
            var gates = new List<CityParkGateDescriptor>(
                source.Gates.Count);
            for (int index = 0; index < source.Gates.Count; index++)
            {
                CityParkGateDescriptor gate = source.Gates[index];
                float datum = SampleRoadOrGround(
                    elevation,
                    gate.Center);
                gates.Add(new CityParkGateDescriptor(
                    gate.Id,
                    WithY(gate.Center, datum),
                    gate.OutwardNormal,
                    gate.Width));
            }

            var trees = new List<Vector3>(source.TreePositions.Count);
            for (int index = 0; index < source.TreePositions.Count; index++)
            {
                Vector3 position = source.TreePositions[index];
                trees.Add(WithY(
                    position,
                    SampleGround(elevation, position, centerDatum)));
            }

            var benches = new List<Vector3>(source.BenchPositions.Count);
            for (int index = 0; index < source.BenchPositions.Count; index++)
            {
                Vector3 position = source.BenchPositions[index];
                benches.Add(WithY(
                    position,
                    SampleGround(elevation, position, centerDatum)));
            }

            return new CityParkPlan(
                new List<Vector2Int>(source.Cells),
                source.WalkableBounds,
                center,
                gates,
                trees,
                benches);
        }

        internal static List<CityDistrictPointOfInterestDescriptor>
            RebaseDistrictPoints(
                CityElevationPlan elevation,
                IReadOnlyList<CityDistrictPointOfInterestDescriptor> source)
        {
            var result = new List<
                CityDistrictPointOfInterestDescriptor>(source.Count);
            for (int index = 0; index < source.Count; index++)
            {
                CityDistrictPointOfInterestDescriptor point = source[index];
                float datum = elevation.GetCellElevation(point.Cell);
                var accesses = new List<
                    CityDistrictPointOfInterestAccessDescriptor>(
                    point.Accesses.Count);
                for (int accessIndex = 0;
                     accessIndex < point.Accesses.Count;
                     accessIndex++)
                {
                    CityDistrictPointOfInterestAccessDescriptor access =
                        point.Accesses[accessIndex];
                    float accessDatum = SampleEdgeAtWorldPoint(
                        elevation,
                        access.FrontageEdge,
                        access.Center);
                    if (Mathf.Abs(accessDatum - datum) > 0.02f)
                    {
                        continue;
                    }

                    accesses.Add(
                        new CityDistrictPointOfInterestAccessDescriptor(
                            access.Id,
                            access.StreetSideDirection,
                            WithY(access.Center, accessDatum),
                            access.OutwardNormal,
                            access.Width,
                            access.ApproachBounds,
                            access.FrontageEdge));
                }

                result.Add(
                    new CityDistrictPointOfInterestDescriptor(
                        point.Id,
                        point.AreaId,
                        point.District,
                        point.Kind,
                        point.Cell,
                        WithY(point.Center, datum),
                        point.PublicBounds,
                        accesses));
            }

            return result;
        }

        internal static List<CitySurfaceDescriptor> RebaseSurfaces(
            CityElevationPlan elevation,
            IReadOnlyList<CitySurfaceDescriptor> source)
        {
            var result = new List<CitySurfaceDescriptor>(source.Count);
            for (int index = 0; index < source.Count; index++)
            {
                CitySurfaceDescriptor surface = source[index];
                result.Add(new CitySurfaceDescriptor(
                    surface.AreaId,
                    surface.Feature,
                    surface.Kind,
                    surface.Cell,
                    surface.WorldBounds,
                    elevation.GetCellElevation(surface.Cell),
                    surface.MapColor,
                    surface.IsWalkable));
            }

            return result;
        }

        internal static List<CityOpenAreaAccessDescriptor>
            RebaseOpenAreaAccesses(
                CityElevationPlan elevation,
                IReadOnlyList<CityOpenAreaAccessDescriptor> source)
        {
            var result = new List<CityOpenAreaAccessDescriptor>(
                source.Count);
            for (int index = 0; index < source.Count; index++)
            {
                CityOpenAreaAccessDescriptor access = source[index];
                float datum = SampleEdgeAtWorldPoint(
                    elevation,
                    access.FrontageEdge,
                    access.Center);
                result.Add(new CityOpenAreaAccessDescriptor(
                    access.Id,
                    access.AreaId,
                    access.Feature,
                    access.Cell,
                    access.StreetSideDirection,
                    access.FrontageEdge,
                    WithY(access.Center, datum),
                    access.OutwardNormal,
                    access.Width,
                    access.ApproachBounds));
            }

            return result;
        }

        private static float SampleEdgeAtWorldPoint(
            CityElevationPlan elevation,
            RoadEdge edge,
            Vector3 position)
        {
            Vector2 start = NodeWorldXZ(elevation, edge.A);
            Vector2 end = NodeWorldXZ(elevation, edge.B);
            Vector2 delta = end - start;
            Vector2 point = new Vector2(position.x, position.z);
            float amount = delta.sqrMagnitude > 0.000001f
                ? Mathf.Clamp01(
                    Vector2.Dot(point - start, delta) /
                    delta.sqrMagnitude)
                : 0f;
            return elevation.SampleRoadDatum(edge, amount);
        }

        private static float SampleRoadOrGround(
            CityElevationPlan elevation,
            Vector3 position,
            float fallback = 0f)
        {
            var xz = new Vector2(position.x, position.z);
            if (elevation.TrySampleSurface(
                    xz,
                    CitySurfaceRole.RoadDatum,
                    out float road,
                    out _))
            {
                return road;
            }

            return SampleGround(elevation, position, fallback);
        }

        private static float SampleGround(
            CityElevationPlan elevation,
            Vector3 position,
            float fallback)
        {
            return elevation.TrySampleSurface(
                new Vector2(position.x, position.z),
                CitySurfaceRole.GroundDatum,
                out float ground,
                out _)
                ? ground
                : fallback;
        }

        private static float AverageCellElevation(
            CityElevationPlan elevation,
            IReadOnlyList<Vector2Int> cells)
        {
            if (cells.Count == 0)
            {
                return 0f;
            }

            float sum = 0f;
            for (int index = 0; index < cells.Count; index++)
            {
                sum += elevation.GetCellElevation(cells[index]);
            }

            return sum / cells.Count;
        }

        private static Vector2 NodeWorldXZ(
            CityElevationPlan elevation,
            Vector2Int node)
        {
            return new Vector2(
                elevation.WorldOrigin.x + node.x * elevation.NodeSpacing.x,
                elevation.WorldOrigin.z + node.y * elevation.NodeSpacing.y);
        }

        private static Vector3 WithY(Vector3 value, float y)
        {
            value.y = y;
            return value;
        }
    }
}
