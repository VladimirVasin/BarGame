using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Final street fixtures use completed footways and bus reservations. Transport and
    /// terrain planning stay upstream so surveying a doorstep cannot recursively reroute a bus.</summary>
    internal static class CityStreetUtilitySites
    {
        private readonly struct Site
        {
            internal readonly bool Phone;
            internal readonly RoadEdge Road;
            internal readonly Vector3 Anchor;
            internal Site(bool phone, RoadEdge road, Vector3 anchor)
            { Phone = phone; Road = road; Anchor = anchor; }
        }

        internal static CityDecorationPlan Survey(CityLayout layout, CityDecorationPlan provisional,
            RoadWalkableArea walk, CityNightFixturePlan night)
        {
            var kept = new List<CityDecorationDescriptor>(provisional.Count);
            var occupied = new List<Rect>();
            var sites = new List<Site>();
            var shapes = new List<Rect>();
            var boxes = new List<Bounds>();
            foreach (var d in provisional.Descriptors)
            {
                if (CityStreetUtilityModel.IsUtility(d.Kind) && !d.Id.Contains("-homeyard-")) continue;
                kept.Add(d);
                CityBusPlatformClearance.AddDecorationFootprints(layout,d,shapes,boxes);
                occupied.AddRange(shapes);
                if (CityStreetUtilityModel.IsUtility(d.Kind) && HomeYardSitePlanner.TryCreate(layout,out var yard) &&
                    layout.TryGetFrontageEdge(yard.Anchor,out var edge))
                    sites.Add(new Site(d.Kind == CityDecorationKind.RoadsidePhoneBooth,edge,Anchor(edge,d.Position)));
            }
            occupied.AddRange(CityBusNetworkFurniture.Reservations(CityDistrictBusPlanner.CreateRoadRouting(layout),layout));
            var kiosks = CityKioskSites.ForLayout(layout);
            foreach (var kiosk in kiosks.Sites) occupied.Add(kiosk.ReservedBounds);
            var fixedClearance = CityBusPlatformClearance.ForLayout(layout);
            var paving = CityStreetSupportPlan.ForLayout(layout);
            var fence = RoadFencePlanner.CreatePlan(layout);
            foreach (var lot in layout.BuildingLots)
            {
                if (!lot.IsOrdinaryBuilding || !layout.TryGetFrontageEdge(lot,out var road) ||
                    layout.GetPathKind(road) != CityPathKind.Street ||
                    CityTerraceDistrictPlan.ForLayout(layout).IsReservedCell(lot.Cell)) continue;
                bool publicUse = false;
                foreach (var poi in layout.DistrictPointsOfInterest)
                    foreach (var door in poi.Accesses)
                        if (door.FrontageEdge == road || (door.Center-lot.Center).sqrMagnitude < 36f*36f) publicUse = true;
                foreach (var kiosk in kiosks.Sites)
                    if (!kiosk.IsRidge && (lot.Center-kiosk.Origin).sqrMagnitude < 36f*36f) publicUse = true;
                if ((lot.Center-layout.Supermarket.Center).sqrMagnitude < 36f*36f) publicUse = true;
                if (publicUse) Place(true);
                if (lot.District == CityDistrictKind.Residential || lot.District == CityDistrictKind.Industrial) Place(false);

                void Place(bool phone)
                {
                    if (kept.Count >= CityDecorationPlan.MaximumDescriptorCount) return;
                    var path = layout.RoadGeometry.Get(road);
                    var frontage = path.Project(XZ(lot.Center));
                    float sign = Mathf.Sign(frontage.SignedLateral);
                    for (int attempt = 0; attempt < 4; attempt++)
                    {
                        float side = (attempt%2==0 ? 1f : -1f)*(phone ? -1f : 1f);
                        float offset = side * (attempt<2 ? 4.1f : 6.8f);
                        float station = Mathf.Clamp(frontage.DistanceAlong+offset,2f,path.Length-2f);
                        var sample = path.SampleDistance(station);
                        Vector2 outward = sign*sample.Right;
                        float kerb = layout.RoadWidth*.5f-CityStreetSurfacePlanner.SidewalkWidth;
                        float width = CityStreetSurfacePlanner.GetPublicSidewalkWidth(layout,road,(int)sign);
                        Vector2 curb = sample.Position + outward*(kerb+.35f);
                        Vector2 center = sample.Position + outward*(kerb+width+1.08f);
                        Vector3 front = new(-outward.x,0f,-outward.y);
                        Vector3 axis = Anchor(road,new Vector3(center.x,0f,center.y));
                        bool crowded = false;
                        foreach (var other in sites)
                            if (other.Phone == phone && (other.Anchor-axis).sqrMagnitude < (phone?85f*85f:57f*57f) &&
                                CityTravelDistance.BetweenAnchors(layout,road,axis,other.Road,other.Anchor) < (phone?85f:57f))
                            { crowded = true; break; }
                        if (crowded) return;
                        float half = phone ? .77f : 1.04f;
                        Rect envelope = Strip(center + outward*.64f,curb,half);
                        if (occupied.Exists(rect => rect.Overlaps(envelope)) ||
                            CityBuildingEntrancePlan.IntersectsReservedApproach(layout,envelope) ||
                            !fixedClearance.IsFixedFootprintClear(envelope) || !fixedClearance.IsStreetLampClear(envelope)) continue;
                        if (!Height(center,out float top)) continue;
                        Vector3 position = new(center.x,top,center.y);
                        if (CityTerraceSurfaceCuts.Reserves(layout,position,1.5f) ||
                            CityDecorationValidator.IsProtectedGroundAnchor(position,half,fence,night)) continue;
                        bool clear = true;
                        for (int row = -1; row <= 1 && clear; row++)
                        {
                            Vector2 lateral = sample.Tangent*(row*half*.9f);
                            if (!Height(center+lateral+outward*.51f,out float baseY) || Mathf.Abs(baseY-top)>.12f)
                            { clear = false; break; }
                            Vector2 end = center+lateral;
                            Vector2 start = curb+lateral;
                            float last = top;
                            int steps = Mathf.CeilToInt(Vector2.Distance(start,end)/.28f);
                            for (int i = 0; i<=steps; i++)
                            {
                                Vector2 p = Vector2.Lerp(end,start,(float)i/steps);
                                if (!Height(p,out float y) || Mathf.Abs(y-last) > (phone?.22f:.14f) ||
                                    !walk.Contains(new Vector3(p.x,y,p.y),.32f)) { clear=false; break; }
                                last=y;
                            }
                        }
                        if (!clear) continue;
                        var kind = phone ? CityDecorationKind.RoadsidePhoneBooth : CityDecorationKind.RoadsideDumpsterAndUtility;
                        kept.Add(new CityDecorationDescriptor($"utility-{layout.Seed}-{lot.Cell.x}-{lot.Cell.y}-{(phone?"phone":"bin")}",
                            kind,CityDecorationAnchorKind.Roadside,lot.District,CityDecorationDescriptor.NoLot,position,front,
                            0,CityDecorationPalette.StreetUtility,CityDecorationVisibilityTier.MidRange,CityDecorationCollisionTier.Blocking,
                            fixedSupportPlane:true, publicApproach:new Vector3(curb.x,lastHeight(curb),curb.y)));
                        occupied.Add(envelope);
                        sites.Add(new Site(phone,road,axis));
                        return;
                    }
                }
            }
            return new CityDecorationPlan(layout.Seed,kept);

            bool Height(Vector2 p,out float y) => paving.TrySampleTop(p,out y,out _) ||
                CityTerrainSurfacePlan.TrySampleGroundTop(layout,p,out y,out _);
            float lastHeight(Vector2 p) { Height(p,out float y); return y; }
            Vector3 Anchor(RoadEdge edge, Vector3 point)
            {
                var path = layout.RoadGeometry.Get(edge);
                var p = path.Project(XZ(point));
                return new Vector3(p.Position.x,layout.ElevationPlan.SampleRoadDatum(edge,p.DistanceAlong/path.Length),p.Position.y);
            }
        }

        internal static Vector2 XZ(Vector3 p) => new(p.x,p.z);
        internal static Rect Strip(Vector2 a,Vector2 b,float halfWidth)
        {
            Vector2 side = new Vector2(b.y-a.y,a.x-b.x).normalized*halfWidth;
            var low = Vector2.Min(a,b)-new Vector2(Mathf.Abs(side.x),Mathf.Abs(side.y));
            var high = Vector2.Max(a,b)+new Vector2(Mathf.Abs(side.x),Mathf.Abs(side.y));
            return Rect.MinMaxRect(low.x,low.y,high.x,high.y);
        }
    }
}
