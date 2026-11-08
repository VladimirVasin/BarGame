using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The ground inside the eastern bend at the end of Court Bridge.
    /// The Blender manifest owns the metric cabin, entrance and furniture footprints.</summary>
    public sealed class CityCobblerSitePlan
    {
        public const string StableId = "ridge-cobbler";
        private static readonly ConditionalWeakTable<CityLayout, CityCobblerSitePlan> Sites = new();
        private static readonly Vector2 Centre = new(68.8f, -307f);
        private static readonly Vector3 Outside = new(2.33f, 0f, 2.47f);
        private readonly Quaternion inverseRotation;
        private readonly List<Bounds> floors = new(4);

        public CityLayout Layout { get; }
        public bool IsEnabled { get; }
        public Vector3 Origin { get; }
        public Quaternion Rotation { get; } = Quaternion.Euler(0f, 250f, 0f);
        public Vector3 Approach { get; }
        public CityCobblerYardPlan Yard { get; }
        public Rect Bounds { get; }
        public Vector2[] Footprint { get; } = Array.Empty<Vector2>();
        public IReadOnlyList<Vector2[]> SolidPolygons { get; } = Array.Empty<Vector2[]>();

        public static CityCobblerSitePlan ForLayout(CityLayout layout) =>
            Sites.GetValue(layout, value => new CityCobblerSitePlan(value));

        private CityCobblerSitePlan(CityLayout layout)
        {
            Layout = layout;
            inverseRotation = Quaternion.Inverse(Rotation);
            if (!layout.HasSummitHome) return;
            var ground = CityHillsideGroundPlan.ForLayout(layout);
            var doorOffset = Rotation * Outside;
            var approach = Centre + new Vector2(doorOffset.x, doorOffset.z);
            if (!ground.Contains(approach, CityGroundTraversalPlanner.MaximumAgentRadius) ||
                !ground.TrySampleTop(approach, out var grade))
                throw new InvalidOperationException("The cobbler entrance has lost its footing.");
            Origin = new Vector3(Centre.x, grade, Centre.y);
            Approach = ToWorld(Outside);

            var manifest = CityCobblerAssetProvider.Geometry;
            Vector3 body = manifest.BodySize;
            Footprint = Polygon(new Bounds(Vector3.zero, body));
            var solids = new List<Vector2[]>();
            var parcel = new List<Vector2[]> { Footprint };
            foreach (var box in manifest.boxes)
            {
                var metric = box.MetricBounds;
                if (IsFloor(box.name))
                {
                    floors.Add(metric);
                    if (box.name != "COL_Floor") parcel.Add(Polygon(metric));
                }
                else if (box.name != "COL_Roof" && box.name != "COL_Door" &&
                    !box.name.StartsWith("COL_Support_", StringComparison.Ordinal))
                    solids.Add(Polygon(metric));
            }
            if (floors.Count != 4)
                throw new InvalidOperationException("The cobbler's floor, landing and two treads are required.");
            Yard = new CityCobblerYardPlan(this);
            solids.AddRange(Yard.SolidPolygons);
            SolidPolygons = solids.AsReadOnly();

            var envelope = CityRoadPolygon.Bounds(Footprint);
            foreach (var polygon in parcel)
            foreach (var p in polygon)
                envelope = Rect.MinMaxRect(Mathf.Min(envelope.xMin, p.x), Mathf.Min(envelope.yMin, p.y),
                    Mathf.Max(envelope.xMax, p.x), Mathf.Max(envelope.yMax, p.y));
            Bounds = envelope;
            parcel.AddRange(Yard.SolidPolygons);
            CheckClearance(parcel);
            IsEnabled = true;
        }

        public Vector3 ToWorld(Vector3 metric) => Origin + Rotation * metric;
        public Vector3 ToLocal(Vector3 world) => inverseRotation * (world - Origin);

        public bool TrySampleFloor(Vector2 worldXZ, out float top)
        {
            top = 0f;
            if (!IsEnabled || !CityHillsideBuilding.Inside(Bounds, worldXZ)) return false;
            var local = ToLocal(new Vector3(worldXZ.x, Origin.y, worldXZ.y));
            bool found = false;
            foreach (var slab in floors)
            {
                var min = slab.min; var max = slab.max;
                if (local.x < min.x || local.x > max.x || local.z < min.z || local.z > max.z) continue;
                top = found ? Mathf.Max(top, Origin.y + max.y) : Origin.y + max.y;
                found = true;
            }
            return found;
        }

        internal static bool IsFloor(string name) =>
            name == "COL_Floor" || name == "COL_Landing" || name == "COL_Tread1" || name == "COL_Tread2";

        private Vector2[] Polygon(Bounds metric)
        {
            var min = metric.min; var max = metric.max;
            return CityRoadPolygon.CounterClockwise(new[] { XZ(min.x, min.z), XZ(max.x, min.z),
                XZ(max.x, max.z), XZ(min.x, max.z) });
            Vector2 XZ(float x, float z)
            {
                var world = ToWorld(new Vector3(x, 0f, z));
                return new Vector2(world.x, world.z);
            }
        }

        private void CheckClearance(List<Vector2[]> parcel)
        {
            Rect clearanceBounds = Bounds;
            foreach (var polygon in parcel)
            foreach (var p in polygon)
                clearanceBounds = Rect.MinMaxRect(Mathf.Min(clearanceBounds.xMin, p.x), Mathf.Min(clearanceBounds.yMin, p.y),
                    Mathf.Max(clearanceBounds.xMax, p.x), Mathf.Max(clearanceBounds.yMax, p.y));
            var furniture = CityHillsideFurniturePlan.ForLayout(Layout);
            foreach (var stop in furniture.BusReservations) CheckRect(stop);
            foreach (var placement in furniture.Placements) CheckRect(placement.AccessBounds);
            foreach (var house in CityHillsideNeighborhoodPlan.ForLayout(Layout).AllBuildings)
            {
                CheckRect(house.FoundationBounds); CheckRect(house.ApproachBounds); CheckRect(house.CourtBounds);
            }
            foreach (var stair in CityHillsideAccessPlan.ForLayout(Layout).AllStairs)
            foreach (var floor in stair.WalkableBounds) CheckRect(floor);
            foreach (var site in CityHillsideSiteDetails.ForLayout(Layout)) CheckRect(site.Bounds);
            foreach (var route in CityHillsideStreetPlan.ForLayout(Layout).Routes)
            foreach (var piece in route.WalkablePolygons)
                if (clearanceBounds.Overlaps(CityRoadPolygon.Bounds(piece))) CheckPolygon(piece);

            void CheckRect(Rect reserved)
            {
                if (clearanceBounds.Overlaps(reserved)) CheckPolygon(CityRoadPolygon.Rectangle(reserved));
            }
            void CheckPolygon(Vector2[] reserved)
            {
                foreach (var footprint in parcel)
                {
                    IReadOnlyList<Vector2> intersection = footprint;
                    for (int i = 0; i < reserved.Length && intersection.Count >= 3; i++)
                        intersection = CityRoadPolygon.Clip(intersection, reserved[i], reserved[(i + 1) % reserved.Length]);
                    if (CityRoadPolygon.Area(intersection) > .0001f)
                        throw new InvalidOperationException("Cobbler workshop meets reserved ground at " + CityRoadPolygon.Bounds(reserved));
                }
            }
        }
    }
}
