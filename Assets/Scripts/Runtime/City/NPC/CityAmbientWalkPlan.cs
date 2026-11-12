using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Short background walks reuse the street graph and its authored pavement heights.</summary>
    internal sealed class CityAmbientWalkPlan
    {
        private const float CellSize = 16f;
        private const float EntrySpacing = 3.5f;
        private const float PlatformClearance = 2.5f;
        private readonly CityPedestrianPlan streets;
        private readonly float[] lengths;
        private readonly bool[] busy;
        private readonly Dictionary<Vector2Int, List<Entry>> cells = new Dictionary<Vector2Int, List<Entry>>();

        internal readonly struct Entry
        {
            public readonly int Link;
            public readonly float Distance;
            public readonly Vector3 Position;

            public Entry(int link, float distance, Vector3 position)
            {
                Link = link;
                Distance = distance;
                Position = position;
            }
        }

        public CityAmbientWalkPlan(CityPedestrianPlan streets, CityLayout layout = null,
            IReadOnlyList<CityBusPlan> busPlans = null)
        {
            this.streets = streets;
            lengths = new float[streets.Links.Count];
            busy = new bool[lengths.Length];
            var platforms = new List<Vector3>();
            var publicEntrances = new List<Vector3>();
            if (layout != null)
            {
                if (busPlans != null)
                    foreach (var platform in CityBusNetworkFurniture.Platforms(busPlans))
                        platforms.Add(CityBusStopWaitPlanner.StandingPosition(platform.line, platform.stop, layout));
                foreach (BuildingLot lot in layout.BuildingLots)
                    if (lot.IsSupermarket || lot.IsBar) publicEntrances.Add(lot.SidewalkArrivalPosition);
            }

            for (int index = 0; index < lengths.Length; index++)
            {
                CityPedestrianLink link = streets.Links[index];
                if ((link.Kind != CityPedestrianLinkKind.Sidewalk && link.Kind != CityPedestrianLinkKind.Turn) ||
                    link.Id.StartsWith("stair:", StringComparison.Ordinal) ||
                    link.Id.StartsWith("hill:quarter:passage", StringComparison.Ordinal))
                    continue;

                Vector3 first = streets.Nodes[link.FirstNodeIndex].Position;
                Vector3 last = streets.Nodes[link.SecondNodeIndex].Position;
                float length = link.Path?.Length ?? Vector2.Distance(Xz(first), Xz(last));
                if (length < .15f || Mathf.Abs(first.y - last.y) / length > .22f ||
                    NearPlatform(link, first, last, platforms))
                    continue;

                lengths[index] = length;
                Vector3 middle = Sample(index, length * .5f, out _);
                busy[index] = link.Id.StartsWith("coast", StringComparison.Ordinal) ||
                    link.Id.StartsWith("river-promenade:", StringComparison.Ordinal) ||
                    (layout != null &&
                     layout.TryGetArea(Vector2Int.FloorToInt(layout.GetWorldGridCoordinate(middle)), out CityAreaDefinition area) &&
                     (area.Feature == CityAreaFeatureKind.CentralPark || area.Feature == CityAreaFeatureKind.NorthWaterfront)) ||
                    NearEntrance(middle, publicEntrances);
                if (link.Kind != CityPedestrianLinkKind.Sidewalk || length < 5f) continue;
                int count = Mathf.Max(1, Mathf.FloorToInt(length / EntrySpacing));
                for (int i = 0; i < count; i++)
                {
                    float distance = length * (i + .5f) / count;
                    Vector3 position = Sample(index, distance, out _);
                    Vector2Int cell = CellAt(position);
                    if (!cells.TryGetValue(cell, out List<Entry> entries))
                        cells.Add(cell, entries = new List<Entry>());
                    entries.Add(new Entry(index, distance, position));
                }
            }
        }

        public float Length(int link) => lengths[link];
        public string LinkId(int link) => streets.Links[link].Id;
        public bool IsBusy(int link) => busy[link];
        public int LaneLimit(int link) => busy[link] && lengths[link] >= 14f ? 2 : 1;

        public void CollectEntries(Vector3 center, float radius, List<Entry> buffer)
        {
            buffer.Clear();
            Vector2Int first = CellAt(center - Vector3.one * radius);
            Vector2Int last = CellAt(center + Vector3.one * radius);
            for (int x = first.x; x <= last.x; x++)
            for (int y = first.y; y <= last.y; y++)
                if (cells.TryGetValue(new Vector2Int(x, y), out List<Entry> entries))
                    buffer.AddRange(entries);
        }

        public IReadOnlyList<int> GetContinuations(int linkIndex, int direction, out int node)
        {
            CityPedestrianLink link = streets.Links[linkIndex];
            node = direction > 0 ? link.SecondNodeIndex : link.FirstNodeIndex;
            return streets.GetLinkIndices(node);
        }

        public int DirectionFrom(int link, int node) => streets.Links[link].FirstNodeIndex == node ? 1 : -1;

        public float ProjectDistance(int linkIndex, Vector3 position)
        {
            CityPedestrianLink link = streets.Links[linkIndex];
            if (link.Path != null) return link.Path.Project(Xz(position)).DistanceAlong;
            Vector2 from = Xz(streets.Nodes[link.FirstNodeIndex].Position);
            Vector2 direction = (Xz(streets.Nodes[link.SecondNodeIndex].Position) - from).normalized;
            return Mathf.Clamp(Vector2.Dot(Xz(position) - from, direction), 0f, lengths[linkIndex]);
        }

        public Vector3 Sample(int linkIndex, float distance, out Vector3 tangent)
        {
            CityPedestrianLink link = streets.Links[linkIndex];
            Vector3 first = streets.Nodes[link.FirstNodeIndex].Position;
            Vector3 last = streets.Nodes[link.SecondNodeIndex].Position;
            float fraction = Mathf.Clamp01(distance / lengths[linkIndex]);
            if (link.Path == null)
            {
                tangent = Vector3.ProjectOnPlane(last - first, Vector3.up).normalized;
                return Vector3.Lerp(first, last, fraction);
            }
            CityRoadSample sample = link.Path.SampleDistance(distance);
            tangent = new Vector3(sample.Tangent.x, 0f, sample.Tangent.y);
            return new Vector3(sample.Position.x, link.PathHeightSampler != null
                ? link.PathHeightSampler(sample.Position) : Mathf.Lerp(first.y, last.y, fraction), sample.Position.y);
        }

        private static bool NearPlatform(CityPedestrianLink link, Vector3 first, Vector3 last,
            List<Vector3> platforms)
        {
            Vector2 start = Xz(first);
            Vector2 delta = Xz(last) - start;
            foreach (Vector3 platform in platforms)
            {
                if (platform.y < Mathf.Min(first.y, last.y) - 1.6f ||
                    platform.y > Mathf.Max(first.y, last.y) + 1.6f)
                    continue;
                Vector2 point = Xz(platform);
                float distanceSquared = link.Path != null ? link.Path.Project(point).DistanceSquared :
                    (point - (start + delta * Mathf.Clamp01(Vector2.Dot(point - start, delta) / delta.sqrMagnitude))).sqrMagnitude;
                if (distanceSquared < PlatformClearance * PlatformClearance) return true;
            }
            return false;
        }

        private static bool NearEntrance(Vector3 point, List<Vector3> entrances)
        {
            foreach (Vector3 entrance in entrances)
                if ((point - entrance).sqrMagnitude < 18f * 18f) return true;
            return false;
        }

        private static Vector2 Xz(Vector3 position) => new Vector2(position.x, position.z);
        private static Vector2Int CellAt(Vector3 position) =>
            new Vector2Int(Mathf.FloorToInt(position.x / CellSize), Mathf.FloorToInt(position.z / CellSize));
    }
}
