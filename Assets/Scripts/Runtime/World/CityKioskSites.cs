using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One bounded street kiosk in each active urban district, in addition to Ridge's existing shop.
    /// Selects only from upstream roads, ground and obstructions; never consumes the final world plan.</summary>
    public sealed class CityKioskSites
    {
        public const float MinimumResidentialBarRouteDistance = 120f;
        public const float MinimumResidentialBarPlanarDistance = 60f;
        private static readonly ConditionalWeakTable<CityLayout, CityKioskSites> Plans = new();
        private readonly List<Rect> reservations = new();
        private readonly List<Rect> walkingRoutes = new();
        public IReadOnlyList<CityKioskSitePlan> Sites { get; }

        public static CityKioskSites ForLayout(CityLayout layout) =>
            Plans.GetValue(layout ?? throw new ArgumentNullException(nameof(layout)), value => new CityKioskSites(value));

        private CityKioskSites(CityLayout layout)
        {
            var sites = new List<CityKioskSitePlan>(3);
            if (RidgeKioskSitePlan.TryCreate(layout, out var ridge))
            { sites.Add(ridge); reservations.Add(ridge.ReservedBounds); }
            var districts = new List<CityDistrictDescriptor>(2);
            var kinds = new HashSet<CityDistrictKind>();
            foreach (var district in layout.Districts)
                if (ActiveUrban(layout, district) && TryIdentity(district.Kind, out _, out _) && kinds.Add(district.Kind))
                    districts.Add(district);
            if (districts.Count == 0) { Sites = sites.AsReadOnly(); return; }
            var clearance = CityBusPlatformClearance.ForLayout(layout);
            var paving = CityStreetSupportPlan.ForLayout(layout);
            var barDistances = new BarDistances(layout);
            foreach (Rect reserve in CityBusNetworkFurniture.Reservations(CityDistrictBusPlanner.CreateRoadRouting(layout), layout))
                reservations.Add(Expanded(reserve, .35f));
            foreach (Rect solid in CityCoastalInfrastructurePlan.ForLayout(layout).SolidFootprints)
                reservations.Add(Expanded(solid, .35f));
            foreach (var path in layout.CourtyardPaths)
                foreach (var strip in path.Ribbon(2f)) walkingRoutes.Add(CityRoadPolygon.Bounds(strip));
            var terrace = CityTerraceDistrictPlan.ForLayout(layout);
            if (terrace.IsEnabled)
            {
                reservations.Add(terrace.ReservedBounds); reservations.Add(terrace.LowerCourtBounds);
                reservations.Add(terrace.LowerCourtEntryBounds); reservations.Add(terrace.LowerPassBounds);
                reservations.AddRange(terrace.PassageFootprints);
            }
            // The only production urban districts are Residential and Industrial.
            // Iterate actual descriptors: a park or an absent legacy enum is never a shop district.
            foreach (var district in districts)
            {
                TryIdentity(district.Kind, out string id, out string character);
                CityKioskSitePlan site = Survey(layout, district, id, character, clearance, paving, barDistances,
                    out string diagnostics);
                if (site != null) { sites.Add(site); reservations.Add(site.ReservedBounds); }
                else if (layout.BlueprintId == CityBlueprintCatalog.DefaultBlueprintId)
                    throw new InvalidOperationException("No clear, supported street kiosk site in " + district.AreaId +
                        ". " + diagnostics);
            }
            Sites = sites.AsReadOnly();
            reservations.Clear(); reservations.TrimExcess();
            walkingRoutes.Clear(); walkingRoutes.TrimExcess();
        }

        private CityKioskSitePlan Survey(CityLayout layout, CityDistrictDescriptor district, string id,
            string character, CityBusPlatformClearance clearance, CityStreetSupportPlan paving, BarDistances barDistances,
            out string diagnostics)
        {
            var ground = new List<Vector2[]>();
            foreach (var cell in district.Cells) ground.AddRange(layout.RoadGeometry.GetGroundPolygons(cell));
            CityKioskSitePlan best = null;
            float bestDistance = float.NegativeInfinity;
            var survey = new SurveyDiagnostics();
            // One-metre stations and two sides of a finite street graph. Leave
            // junctions alone and retain the complete existing through pavement.
            foreach (RoadEdge edge in layout.RoadEdges)
            {
                if (layout.GetPathKind(edge) != CityPathKind.Street || layout.IsRoadBridgeEdge(edge)) continue;
                CityRoadPath path = layout.RoadGeometry.Get(edge);
                for (float station = 7f; station <= path.Length - 7f; station += 1f)
                for (int side = -1; side <= 1; side += 2)
                {
                    CityRoadSample sample = path.SampleDistance(station);
                    Vector2 away = sample.Right * side;
                    float width = CityStreetSurfacePlanner.GetPublicSidewalkWidth(layout, edge, side);
                    float outer = layout.RoadWidth * .5f - CityStreetSurfacePlanner.SidewalkWidth + width;
                    Vector2 center = sample.Position + away * (outer + CityKioskSitePlan.ApronFront - 1f);
                    survey.LastOrigin = center;
                    if (!InGround(ground, center)) { survey.Reject("notdistrictground"); continue; }
                    Quaternion rotation = Quaternion.LookRotation(new Vector3(-away.x, 0f, -away.y));
                    Vector2 seam = sample.Position + away * (outer - width * .5f);
                    if (!paving.TrySampleTop(seam, FootstepGroundKind.Stone, out float top))
                    { survey.Reject("nopaving"); continue; }
                    Vector3 origin = new Vector3(center.x, top, center.y);
                    var candidate = new CityKioskSitePlan(id, district.Kind, origin, rotation, character, false, edge,
                        new Vector3(sample.Position.x,
                            layout.ElevationPlan.SampleRoadDatum(edge, station / path.Length), sample.Position.y),
                        actualSidewalkWidth: width);
                    if (!Fits(layout, candidate, sample, away, outer, ground, clearance, paving, survey)) continue;
                    float route = barDistances.NearestRoute(edge, station);
                    float planar = barDistances.NearestPlanar(center);
                    survey.RecordDistance(center, route, planar);
                    if (district.Kind == CityDistrictKind.Residential &&
                        (route < MinimumResidentialBarRouteDistance || planar < MinimumResidentialBarPlanarDistance))
                    { survey.Reject("distance"); continue; }
                    // Distance to every bar is part of selection, not a display-only label.
                    float score = float.IsPositiveInfinity(route) ? 0f : route;
                    if (score <= bestDistance) continue;
                    bestDistance = score;
                    best = new CityKioskSitePlan(id, district.Kind, origin, rotation, character, false, edge,
                        candidate.StreetAnchor, route, planar, width);
                }
            }
            diagnostics = survey.ToString();
            return best;
        }

        private bool Fits(CityLayout layout, CityKioskSitePlan site, CityRoadSample sample, Vector2 away,
            float outer, IReadOnlyList<Vector2[]> ground, CityBusPlatformClearance clearance, CityStreetSupportPlan paving,
            SurveyDiagnostics survey)
        {
            if (!clearance.IsStaticFootprintClear(site.ReservedBounds)) return survey.Reject("clearance");
            // Paving may join an existing foot route; a roof/wall cannot occupy
            // it. Treating the shared public sidewalk as an exclusive route
            // reservation rejected otherwise valid street-front aprons.
            if (CityResidentialEntrancePlan.IntersectsReservedApproach(layout, site.StructureBounds))
                return survey.Reject("entrance");
            foreach (Rect route in walkingRoutes)
                if (route.Overlaps(site.StructureBounds)) return survey.Reject("walkingroute");
            foreach (Rect reserve in reservations)
                if (reserve.Overlaps(site.ReservedBounds)) return survey.Reject("reserved");
            Vector2 throughCenter = sample.Position + away * (outer - site.ActualSidewalkWidth * .5f);
            Vector2 tangent = sample.Tangent * 3.2f, halfWidth = away * (site.ActualSidewalkWidth * .5f);
            Rect through = CityRoadPolygon.Bounds(new[] { throughCenter - tangent - halfWidth,
                throughCenter + tangent - halfWidth, throughCenter + tangent + halfWidth,
                throughCenter - tangent + halfWidth });
            if (!clearance.IsStaticFootprintClear(through)) return survey.Reject("throughclearance");
            foreach (Rect reserve in reservations) if (reserve.Overlaps(through)) return survey.Reject("throughreserved");
            // The whole shell/eaves sit on the district's existing ground. The
            // imported slab may bridge at most its own buried foundation depth.
            for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                Vector3 point = site.Origin + site.Rotation * new Vector3(x * 1.62f, 0f, z * 1.36f);
                if (!InGround(ground, Xz(point))) return survey.Reject("foundationground");
                if (!clearance.TryHeight(point, out float soil)) return survey.Reject("foundationunsupported");
                float delta = soil - site.Origin.y;
                survey.MinFoundationDelta = Mathf.Min(survey.MinFoundationDelta, delta);
                survey.MaxFoundationDelta = Mathf.Max(survey.MaxFoundationDelta, delta);
                if (delta > .025f || delta < -.44f) return survey.Reject("foundationHeight");
            }
            // Test both directions of the existing public passage, its full
            // width and its physical height rather than a road AABB.
            for (int along = -3; along <= 3; along++)
            for (int across = 0; across <= 2; across++)
            {
                Vector2 point = sample.Position + sample.Tangent * along + away *
                    (outer - site.ActualSidewalkWidth + .05f + across * (site.ActualSidewalkWidth - .10f) * .5f);
                if (!paving.TrySampleTop(point, FootstepGroundKind.Stone, out float top)) return survey.Reject("throughpaving");
                survey.MaxThroughDelta = Mathf.Max(survey.MaxThroughDelta, Mathf.Abs(top - site.Origin.y));
                if (Mathf.Abs(top - site.Origin.y) > .035f) return survey.Reject("throughHeight");
            }
            return true;
        }

        // Bounded construction diagnostics stay silent on a successful site.
        private sealed class SurveyDiagnostics
        {
            private readonly Dictionary<string, int> rejected = new();
            internal Vector2 LastOrigin;
            internal Vector2 FarthestOrigin;
            internal float FarthestRoute = float.NegativeInfinity;
            internal float FarthestPlanar;
            internal float MinFoundationDelta = float.PositiveInfinity;
            internal float MaxFoundationDelta = float.NegativeInfinity;
            internal float MaxThroughDelta;

            internal bool Reject(string stage)
            {
                rejected.TryGetValue(stage, out int count); rejected[stage] = count + 1;
                return false;
            }
            internal void RecordDistance(Vector2 origin, float route, float planar)
            {
                if (route <= FarthestRoute) return;
                FarthestOrigin = origin; FarthestRoute = route; FarthestPlanar = planar;
            }
            public override string ToString()
            {
                var result = new System.Text.StringBuilder("Rejected:");
                foreach (var pair in rejected) result.Append(' ').Append(pair.Key).Append('=').Append(pair.Value);
                return result.Append("; foundationDelta=").Append(MinFoundationDelta.ToString("F3"))
                    .Append("..").Append(MaxFoundationDelta.ToString("F3"))
                    .Append("; throughDelta=").Append(MaxThroughDelta.ToString("F3"))
                    .Append("; farthestRoute=").Append(FarthestRoute.ToString("F2"))
                    .Append(" planar=").Append(FarthestPlanar.ToString("F2"))
                    .Append(" at=").Append(FarthestOrigin).Append("; last=").Append(LastOrigin).ToString();
            }
        }

        private static bool ActiveUrban(CityLayout layout, CityDistrictDescriptor district)
        {
            foreach (var cell in district.Cells)
                if (layout.TryGetArea(cell, out var area))
                    return area.Category == CityAreaCategory.UrbanBuilt && area.Feature == CityAreaFeatureKind.UrbanDistrict;
            return false;
        }

        // Construction-only distance field. Each reached node asks the shared
        // graph once per bar; metre stations then use its exact grade metric.
        private sealed class BarDistances
        {
            private readonly CityLayout layout;
            private readonly List<(RoadEdge edge, Vector3 anchor, Vector2 door, float along)> bars = new();
            private readonly Dictionary<Vector2Int, float> endpoints = new();

            internal BarDistances(CityLayout layout)
            {
                this.layout = layout;
                foreach (BuildingLot bar in layout.BuildingLots)
                    if (bar.IsBar && layout.TryGetFrontageEdge(bar, out RoadEdge edge))
                        bars.Add((edge, bar.ReturnPosition, Xz(bar.DoorPosition),
                            CityTravelDistance.RoadDistanceAt(layout, edge,
                                layout.RoadGeometry.Get(edge).Project(Xz(bar.ReturnPosition)).DistanceAlong)));
            }

            internal float NearestPlanar(Vector2 point)
            {
                float result = float.PositiveInfinity;
                foreach (var bar in bars) result = Mathf.Min(result, Vector2.Distance(point, bar.door));
                return result;
            }

            internal float NearestRoute(RoadEdge edge, float station)
            {
                if (bars.Count == 0) return float.PositiveInfinity;
                float along = CityTravelDistance.RoadDistanceAt(layout, edge, station);
                float length = CityTravelDistance.RoadDistanceAt(layout, edge, layout.RoadGeometry.Get(edge).Length);
                float result = Mathf.Min(AtEndpoint(edge, false) + along, AtEndpoint(edge, true) + length - along);
                foreach (var bar in bars)
                    if (bar.edge == edge) result = Mathf.Min(result, Mathf.Abs(along - bar.along));
                return result;
            }

            private float AtEndpoint(RoadEdge edge, bool end)
            {
                Vector2Int node = end ? edge.B : edge.A;
                if (endpoints.TryGetValue(node, out float result)) return result;
                CityRoadPath path = layout.RoadGeometry.Get(edge);
                Vector2 point = path.SampleDistance(end ? path.Length : 0f).Position;
                var anchor = new Vector3(point.x, layout.ElevationPlan.SampleRoadDatum(edge, end ? 1f : 0f), point.y);
                result = float.PositiveInfinity;
                foreach (var bar in bars) result = Mathf.Min(result,
                    CityTravelDistance.BetweenAnchors(layout, edge, anchor, bar.edge, bar.anchor));
                endpoints.Add(node, result);
                return result;
            }
        }

        private static bool TryIdentity(CityDistrictKind kind, out string id, out string character)
        {
            if (kind == CityDistrictKind.Residential)
            { id = "residential-kiosk"; character = DefaultNpcPopulation.CityResidentialKioskSeller; return true; }
            if (kind == CityDistrictKind.Industrial)
            { id = "industrial-kiosk"; character = DefaultNpcPopulation.CityIndustrialKioskSeller; return true; }
            id = character = null; return false;
        }

        private static bool InGround(IReadOnlyList<Vector2[]> polygons, Vector2 point)
        {
            foreach (var polygon in polygons) if (CityRoadPolygon.Contains(polygon, point)) return true;
            return false;
        }
        private static Vector2 Xz(Vector3 value) => new Vector2(value.x, value.z);
        private static Rect Expanded(Rect value, float margin) => Rect.MinMaxRect(
            value.xMin - margin, value.yMin - margin, value.xMax + margin, value.yMax + margin);
    }
}
