using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Fixed-metre parcel inside the last eastern hairpin. All public feet use the built hillside.</summary>
    public sealed class CityGlassReturnSitePlan
    {
        public const string StableId = "ridge-glass-return";
        public const float Width = 4.6f, Depth = 3.2f, YardWidth = 4.2f, YardDepth = 3.8f;
        public const float CanopyFront = 2.38f;
        public const float YardSlabClearance = .055f;
        private static readonly Vector2 ShellCentre = new(160.9f, -330.9f);
        private static readonly Vector2 YardCentre = new(161f, -336.2f);
        private static readonly Vector2 ArrivalXZ = new(164.9f, -332.6f);
        private static readonly Quaternion SiteRotation = Quaternion.Euler(0f, 105f, 0f);
        internal static readonly Vector2 ServiceDoorOutside = new(3f, -.48f);
        internal static readonly Vector2 ServiceCorner = new(2.75f, 2.15f);
        private static readonly ConditionalWeakTable<CityLayout, CityGlassReturnSitePlan> Plans = new();
        private static readonly ConditionalWeakTable<CityLayout, PavingPlan> PavingPlans = new();
        // Raw furniture and landscaping are surveyed before the terrain triangles exist.
        // This reserve also covers the canopy and the turn from the footway to the window.
        internal static Rect LandReservation => Rect.MinMaxRect(158.7f, -338.2f, 166f, -328.2f);

        public bool IsEnabled { get; }
        public Vector3 Origin { get; }
        public Quaternion Rotation => SiteRotation;
        public Vector3 YardOrigin { get; }
        public Vector3 Approach { get; }
        public Vector3 CounterWorld => Origin + Rotation * new Vector3(0f, 1.02f, Depth * .5f);
        public Vector3 HeroDock { get; }
        public Vector3 FloodlightWallContact { get; }
        public Vector3 FloodlightTarget { get; }
        public Rect Bounds { get; }
        public Vector2[] Footprint { get; } = Array.Empty<Vector2>();
        public Vector2[] YardFootprint { get; } = Array.Empty<Vector2>();
        public IReadOnlyList<Vector2[]> SolidPolygons { get; } = Array.Empty<Vector2[]>();

        public static CityGlassReturnSitePlan ForLayout(CityLayout layout) =>
            Plans.GetValue(layout, value => new CityGlassReturnSitePlan(value));

        // XZ-only finishes can be planned before the ground mesh exists. The
        // terrain partitioner then keeps the original height and triangles.
        internal static PavingPlan PavingForLayout(CityLayout layout) =>
            PavingPlans.GetValue(layout, CreatePaving);

        internal sealed class PavingPlan
        {
            internal readonly List<Vector2[]> Path = new List<Vector2[]>();
            internal readonly List<Vector2[]> Asphalt = new List<Vector2[]>();
            internal Vector2 SidewalkContact { get; set; }
        }

        private static PavingPlan CreatePaving(CityLayout layout)
        {
            var result = new PavingPlan();
            if (!layout.HasSummitHome) return result;
            CityHillsideStreetRoute street = null;
            foreach (var road in CityHillsideStreetPlan.ForLayout(layout).Routes)
                if (road.Bank == CityTerraceTransportBank.East) { street = road; break; }
            if (street == null) throw new InvalidOperationException("Glass return paving needs the east sidewalk.");

            Vector3 arrival = new(ArrivalXZ.x, 0f, ArrivalXZ.y);
            var first = CityGlassReturnWorkerPlan.Project(street.SidewalkPath, arrival);
            var second = CityGlassReturnWorkerPlan.Project(street.OppositeSidewalkPath, arrival);
            result.SidewalkContact = XZ(first.distance <= second.distance ? first.point : second.point);
            Vector2 corner = SitePoint(ServiceCorner);
            Vector2 overlap = ArrivalXZ + (corner - ArrivalXZ).normalized * .4f;
            var shell = new Vector3(ShellCentre.x, 0f, ShellCentre.y);
            var yard = new Vector3(YardCentre.x, 0f, YardCentre.y);
            var cuts = new List<Vector2[]>
            {
                RectAt(shell, SiteRotation, Width, Depth),
                RectAt(yard, Quaternion.identity, YardWidth, YardDepth)
            };
            foreach (var polygon in street.WalkablePolygons)
                cuts.Add(polygon);

            foreach (var polygon in new CityRoadPath(new[] { result.SidewalkContact, ArrivalXZ, overlap }).Ribbon(1.45f))
                Add(result.Path, polygon);
            foreach (var polygon in new CityRoadPath(new[] { ArrivalXZ, corner }).Ribbon(1.65f))
                Add(result.Asphalt, polygon);
            Add(result.Asphalt, LocalRect(-1.85f, 1.52f, 3.55f, 3.06f));
            Add(result.Asphalt, LocalRect(2.20f, -1.12f, 3.62f, 2.82f));
            return result;

            void Add(List<Vector2[]> destination, Vector2[] polygon)
            {
                var pieces = new List<Vector2[]> { polygon };
                Rect bounds = CityRoadPolygon.Bounds(polygon);
                foreach (var cut in cuts)
                {
                    if (!CityRoadPolygon.Bounds(cut).Overlaps(bounds)) continue;
                    var remaining = new List<Vector2[]>();
                    foreach (var piece in pieces) remaining.AddRange(CityRoadPolygon.Subtract(piece, cut));
                    pieces = remaining;
                }
                destination.AddRange(pieces);
            }
        }

        private static Vector2[] LocalRect(float left, float rear, float right, float front) =>
            CityRoadPolygon.CounterClockwise(new[] { SitePoint(new Vector2(left, rear)), SitePoint(new Vector2(right, rear)),
                SitePoint(new Vector2(right, front)), SitePoint(new Vector2(left, front)) });

        private static Vector2 SitePoint(Vector2 point) =>
            ShellCentre + XZ(SiteRotation * new Vector3(point.x, 0f, point.y));

        private CityGlassReturnSitePlan(CityLayout layout)
        {
            if (!layout.HasSummitHome) return;
            var ground = CityHillsideGroundPlan.ForLayout(layout);
            Origin = Ground(ShellCentre);
            Vector3 yardGround = Ground(YardCentre);
            Rect slab = new(YardCentre - new Vector2(YardWidth, YardDepth) * .5f, new Vector2(YardWidth, YardDepth));
            // The crate floor and the existing earth both used to end at the
            // surveyed ground elevation. Lift the entire fenced slab together.
            // Test the actual terrain triangles, including their diagonals.
            float bed = yardGround.y;
            foreach (var bank in CityHillsideGroundGrid.ForLayout(layout))
            {
                while (bank.ReachesAbove(slab, bed))
                {
                    bed += .025f;
                    if (bed - yardGround.y > .20f)
                        throw new InvalidOperationException("Glass sorting yard exceeds its foundation depth.");
                }
            }
            YardOrigin = new Vector3(yardGround.x, bed + YardSlabClearance, yardGround.z);
            Approach = Ground(ArrivalXZ);
            HeroDock = Ground(XZ(Origin + Rotation * (Vector3.forward * (Depth * .5f + .57f))));
            // The eastern end wall belongs to the same surveyed residential parcel.
            // Keep the mount on its masonry, above the pavilion roof, rather than on
            // the terrain or on an invented free-standing pole.
            CityHillsideBuilding neighbour = null;
            float closest = float.PositiveInfinity;
            foreach (var house in CityHillsideNeighborhoodPlan.ForLayout(layout).Buildings)
            {
                if (house.Bank != CityTerraceTransportBank.East || house.FoundationBounds.xMax >= ShellCentre.x) continue;
                Rect wall = house.FoundationBounds;
                float z = Mathf.Clamp(ShellCentre.y - 2.3f, wall.yMin + .65f, wall.yMax - .65f);
                float distance = (new Vector2(wall.xMax, z) - ShellCentre).sqrMagnitude;
                if (distance >= closest) continue;
                closest = distance; neighbour = house;
            }
            if (neighbour == null) throw new InvalidOperationException("Glass return floodlight has no adjacent masonry wall.");
            Rect facade = neighbour.FoundationBounds;
            FloodlightWallContact = new Vector3(facade.xMax, neighbour.ThresholdWorld.y + 6.4f,
                Mathf.Clamp(ShellCentre.y - 2.3f, facade.yMin + .65f, facade.yMax - .65f));
            FloodlightTarget = (Origin + YardOrigin) * .5f + new Vector3(.75f, .9f, 0f);
            Footprint = RectAt(Origin, Rotation, Width, Depth);
            YardFootprint = RectAt(YardOrigin, Quaternion.identity, YardWidth, YardDepth);
            SolidPolygons = Array.AsReadOnly(new[] { Footprint, YardFootprint });
            Rect body = CityRoadPolygon.Bounds(Footprint), yard = CityRoadPolygon.Bounds(YardFootprint);
            Bounds = Rect.MinMaxRect(Mathf.Min(body.xMin, yard.xMin), Mathf.Min(body.yMin, yard.yMin),
                Mathf.Max(body.xMax, yard.xMax), Mathf.Max(body.yMax, yard.yMax));
            CheckFooting(Footprint, Origin.y);
            CheckFooting(YardFootprint, YardOrigin.y);

            float radius = CityGroundTraversalPlanner.MaximumAgentRadius;
            if (!ground.Contains(XZ(Approach), radius) || !ground.Contains(XZ(HeroDock), radius))
                throw new InvalidOperationException("The public approach to the glass return window lost its footing.");

            void Clear(Rect other)
            {
                if (LandReservation.Overlaps(other))
                    throw new InvalidOperationException("Glass return parcel overlaps reserved space at " + other);
            }
            var furniture = CityHillsideFurniturePlan.ForLayout(layout);
            foreach (Rect stop in furniture.BusReservations) Clear(stop);
            foreach (var item in furniture.Placements) Clear(item.AccessBounds);
            foreach (var detail in CityHillsideSiteDetails.ForLayout(layout)) Clear(detail.Bounds);
            foreach (var house in CityHillsideNeighborhoodPlan.ForLayout(layout).AllBuildings)
            {
                Clear(house.FoundationBounds);
                Clear(house.CourtBounds);
                Clear(house.ApproachBounds);
            }
            foreach (var stair in CityHillsideAccessPlan.ForLayout(layout).AllStairs)
                foreach (Rect piece in stair.WalkableBounds) Clear(piece);
            foreach (var road in CityHillsideStreetPlan.ForLayout(layout).Routes)
            {
                if (!road.WorldBounds.Overlaps(LandReservation)) continue;
                foreach (Vector2[] piece in road.WalkablePolygons)
                {
                    if (!CityRoadPolygon.Bounds(piece).Overlaps(LandReservation)) continue;
                    IReadOnlyList<Vector2> overlap = CityRoadPolygon.Rectangle(LandReservation);
                    for (int i = 0; i < piece.Length && overlap.Count >= 3; i++)
                        overlap = CityRoadPolygon.Clip(overlap, piece[i], piece[(i + 1) % piece.Length]);
                    if (CityRoadPolygon.Area(overlap) > .0001f)
                        throw new InvalidOperationException("Glass return parcel crosses the public footway.");
                }
            }
            IsEnabled = true;

            Vector3 Ground(Vector2 p)
            {
                if (!ground.TrySampleTop(p, out float y))
                    throw new InvalidOperationException("Missing glass return hillside at " + p);
                return new Vector3(p.x, y, p.y);
            }
            void CheckFooting(Vector2[] footprint, float top)
            {
                foreach (var corner in footprint)
                    if (Mathf.Abs(Ground(corner).y - top) > .20f)
                        throw new InvalidOperationException("Glass return plinth exceeds its footing at " + corner);
            }
        }

        private static Vector2[] RectAt(Vector3 centre, Quaternion rotation, float width, float depth)
        {
            Vector3 right = rotation * Vector3.right * (width * .5f);
            Vector3 front = rotation * Vector3.forward * (depth * .5f);
            return CityRoadPolygon.CounterClockwise(new[] { XZ(centre - right - front), XZ(centre + right - front),
                XZ(centre + right + front), XZ(centre - right + front) });
        }

        private static Vector2 XZ(Vector3 p) => new(p.x, p.z);
    }
}
