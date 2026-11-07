using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    public sealed class CityPawnshopDescentLanding
    {
        internal CityPawnshopDescentLanding(string id, Vector3 start, Vector3 end, float width)
        {
            Id = id; Start = start; End = end; Width = width;
            Vector3 right = Vector3.Cross(Vector3.up, (end-start).normalized) * (width*.5f);
            TopVertices = Array.AsReadOnly(new[] {start-right,start+right,end+right,end-right});
            Footprint = CityRoadPolygon.CounterClockwise(new[] {Xz(start-right),Xz(start+right),Xz(end+right),Xz(end-right)});
        }
        public string Id {get;}
        public Vector3 Start {get;}
        public Vector3 End {get;}
        public Vector3 Center => (Start+End)*.5f;
        public float Length => Vector3.Distance(Start,End);
        public float Width {get;}
        public IReadOnlyList<Vector3> TopVertices {get;}
        public Vector2[] Footprint {get;}
        private static Vector2 Xz(Vector3 p)=>new(p.x,p.z);
    }

    /// <summary>A finite municipal stair from the existing Home view bench to the turn's outer footway.</summary>
    public sealed class CityPawnshopDescentPlan
    {
        public const string BenchId=CityHillsideFurniturePlan.HomeBenchId;
        public const float LandingWidth=2.4f;
        private static readonly ConditionalWeakTable<CityLayout,CityPawnshopDescentPlan> Plans=new();
        public static CityPawnshopDescentPlan ForLayout(CityLayout layout)=>
            Plans.GetValue(layout,value=>new CityPawnshopDescentPlan(value));
        public bool IsEnabled {get;}
        public Vector3 BenchDock {get;}
        public Vector3 UpperDock {get;}
        public Vector3 UpperApproachMouth {get;}
        public Vector3 UpperArrivalWorld=>UpperDock;
        public Vector3 LowerDock {get;}
        public Vector3 LowerLandingEnd {get;}
        public Vector3 Direction {get;}
        public Quaternion Rotation=>Quaternion.LookRotation(Direction);
        public Rect Bounds {get;}
        public IReadOnlyList<CityExteriorStairFlightDescriptor> Flights {get;}
        public IReadOnlyList<CityExteriorStairFlightDescriptor> FlightsWorld=>Flights;
        public IReadOnlyList<CityPawnshopDescentLanding> Landings {get;}
        public IReadOnlyList<CityPawnshopDescentLanding> FloorsWorld=>Landings;
        public IReadOnlyList<Vector2[]> Footprints {get;}
        public IReadOnlyList<Vector2[]> SurfacePolygons {get;}
        public IReadOnlyList<Vector2[]> GroundCuts=>SurfacePolygons;
        public IReadOnlyList<Vector2[]> ApproachPolygons {get;}
        public IReadOnlyList<CityPawnshopDescentLanding> ApproachLandings {get;}
        public IReadOnlyList<Vector2[]> SolidPolygons {get;}
        public IReadOnlyList<Vector3> StairRoute {get;}
        public IReadOnlyList<Vector3> RouteWorld {get;}
        public IReadOnlyList<Vector3> PawnshopRoute {get;}
        public IReadOnlyList<Vector3> ToPawnshopRouteWorld=>PawnshopRoute;
        public IReadOnlyList<Vector3> LampFeet {get;}
        private Vector2[] footwaySeam;
        private Vector2[][] flightFootprints=Array.Empty<Vector2[]>();

        private CityPawnshopDescentPlan(CityLayout layout)
        {
            Flights=Array.Empty<CityExteriorStairFlightDescriptor>();Landings=Array.Empty<CityPawnshopDescentLanding>();
            Footprints=Array.Empty<Vector2[]>();StairRoute=RouteWorld=PawnshopRoute=Array.Empty<Vector3>();
            SurfacePolygons=ApproachPolygons=Array.Empty<Vector2[]>();
            ApproachLandings=Array.Empty<CityPawnshopDescentLanding>();
            LampFeet=Array.Empty<Vector3>();
            SolidPolygons=Array.Empty<Vector2[]>();
            if(!CityPawnshopPlan.TryCreate(layout,out var pawnshop)) return;
            CityHillsideFurniturePlacement bench=default;
            foreach(var candidate in CityHillsideFurniturePlan.ForLayout(layout).Placements)
                if(candidate.Id==BenchId) {bench=candidate;break;}
            if(bench.Id==null) return;
            CityHillsideStreetRoute west=null;
            foreach(var road in CityHillsideStreetPlan.ForLayout(layout).Routes)
                if(road.Bank==CityTerraceTransportBank.West) {west=road;break;}
            if(west==null) return;
            var ground=CityHillsideGroundPlan.ForLayout(layout);
            Vector3 right=Vector3.Cross(Vector3.up,bench.Forward);
            // Reuse the established west-end path corner, outside the seat and
            // its standing dock. The old summit approach stays in place.
            UpperDock=bench.Position-right*2f-bench.Forward*1.7f;
            if(!ground.TrySampleTop(Xz(UpperDock),out float upper))
                throw new InvalidOperationException("Pawnshop descent upper dock has no existing bank support.");
            UpperDock=new Vector3(UpperDock.x,upper,UpperDock.z);
            foreach(var seat in CityHillsideFurniturePlan.ForLayout(layout).Seats)
                if(seat.Id==BenchId) {BenchDock=bench.Position+CityBenchSitPlan.GetDockOffset(seat);break;}
            int lowerIndex=west.CourtApex+4;
            LowerDock=west.OppositeSidewalkPath[lowerIndex];
            if(!west.TrySampleTop(Xz(LowerDock),out float lower,out var kind) || kind!=CityStreetSupportKind.Sidewalk)
                throw new InvalidOperationException("Pawnshop descent lower dock must be on its actual outer footway.");
            LowerDock=new Vector3(LowerDock.x,lower,LowerDock.z);
            Vector3 outward=Vector3.ProjectOnPlane(LowerDock-pawnshop.Origin,Vector3.up).normalized;
            float inside=0f,outside=1.5f;
            for(int i=0;i<18;i++)
            {
                float at=(inside+outside)*.5f;
                if(west.TrySampleTop(Xz(LowerDock+outward*at),out _,out var material) &&
                    material==CityStreetSupportKind.Sidewalk) inside=at;else outside=at;
            }
            LowerLandingEnd=LowerDock+outward*Mathf.Max(0f,inside-.02f);
            LowerLandingEnd=new Vector3(LowerLandingEnd.x,lower,LowerLandingEnd.z);
            Vector3 span=Vector3.ProjectOnPlane(LowerLandingEnd-UpperDock,Vector3.up);
            Direction=span.normalized;
            float apron=(span.magnitude-2f*CityTerracePilotPlan.FlightRun-2.4f)*.5f;
            float rise=(upper-lower)*.5f;
            if(apron<1.2f || rise<=0f || rise/CityTerracePilotPlan.StepsPerFlight>.18f)
                throw new InvalidOperationException("Pawnshop descent needs comfortable aprons and ordinary stair rise.");
            Vector3 high=UpperDock+Direction*apron;
            Vector3 first=high+Direction*CityTerracePilotPlan.FlightRun-Vector3.up*rise;
            Vector3 middle=first+Direction*2.4f;
            Vector3 bottom=middle+Direction*CityTerracePilotPlan.FlightRun-Vector3.up*rise;
            var flights=new[] {Flight("Pawnshop descent upper flight",first),Flight("Pawnshop descent lower flight",bottom)};
            Flights=Array.AsReadOnly(flights);
            Landings=Array.AsReadOnly(new[] {
                new CityPawnshopDescentLanding("Descent upper apron",UpperDock,high,LandingWidth),
                new CityPawnshopDescentLanding("Descent view landing",first,middle,LandingWidth),
                new CityPawnshopDescentLanding("Descent lower apron",bottom,LowerLandingEnd,LandingWidth)});
            var footprints=new List<Vector2[]>();
            foreach(var landing in Landings) footprints.Add(landing.Footprint);
            flightFootprints=new[] {FlightPolygon(flights[0]),FlightPolygon(flights[1])};
            footprints.AddRange(flightFootprints);
            Footprints=footprints.AsReadOnly();
            Vector3 pocketCorner=bench.Position-right*2.25f+bench.Forward*1.45f;
            if(!ground.TrySampleTop(Xz(pocketCorner),out float cornerTop))
                throw new InvalidOperationException("Pawnshop descent branch must join the real bench pocket.");
            pocketCorner=new Vector3(pocketCorner.x,cornerTop,pocketCorner.z);
            UpperApproachMouth=UpperDock-Direction*.9f;
            ApproachLandings=Array.AsReadOnly(new[] {
                new CityPawnshopDescentLanding("Descent pocket branch",pocketCorner,UpperApproachMouth,1.6f),
                new CityPawnshopDescentLanding("Descent rear mouth",UpperApproachMouth,UpperDock,1.6f)});
            var approachPolygons=new List<Vector2[]>(2);
            var surfaces=new List<Vector2[]>(footprints);
            foreach(var approach in ApproachLandings) {approachPolygons.Add(approach.Footprint);surfaces.Add(approach.Footprint);}
            ApproachPolygons=approachPolygons.AsReadOnly();SurfacePolygons=surfaces.AsReadOnly();
            Vector2 minimum=new(float.PositiveInfinity,float.PositiveInfinity),maximum=-minimum;
            foreach(var polygon in SurfacePolygons) foreach(var p in polygon) {minimum=Vector2.Min(minimum,p);maximum=Vector2.Max(maximum,p);}
            Bounds=Rect.MinMaxRect(minimum.x,minimum.y,maximum.x,maximum.y);
            footwaySeam=new CityPawnshopDescentLanding("Descent footway seam",LowerLandingEnd,LowerDock,LandingWidth).Footprint;
            ValidateClearance(layout,bench);
            StairRoute=Array.AsReadOnly(new[] {UpperDock,high,first,middle,bottom,LowerLandingEnd,LowerDock});
            var route=new List<Vector3> {BenchDock,pocketCorner,UpperApproachMouth};
            route.AddRange(StairRoute);
            // Continue on the public footway around the bend, then use the
            // already authored south zebra. The stair never enters traffic.
            for(int i=lowerIndex+1;i<=west.CourtApex+8;i++) route.Add(west.OppositeSidewalkPath[i]);
            CityPawnshopCrossing crossing=null;
            foreach(var candidate in CityPawnshopCrosswalkPlan.ForLayout(layout).Crossings)
                if(candidate.Lane==5) {crossing=candidate;break;}
            if(crossing==null) throw new InvalidOperationException("Pawnshop descent needs the existing south crossing.");
            route.Add(crossing.WalkPath[0]);RouteWorld=route.AsReadOnly();
            var doorRoute=new List<Vector3>(route);doorRoute.AddRange(crossing.ApproachPath);PawnshopRoute=doorRoute.AsReadOnly();
            Vector3 railRight=Vector3.Cross(Vector3.up,Direction);
            LampFeet=Array.AsReadOnly(new[] {Landings[0].Center+railRight*1.07f,Landings[1].Center-railRight*1.07f});
            var lampSolids=new List<Vector2[]>(2);
            foreach(Vector3 foot in LampFeet) lampSolids.Add(CityRoadPolygon.Rectangle(
                Rect.MinMaxRect(foot.x-.12f,foot.z-.12f,foot.x+.12f,foot.z+.12f)));
            SolidPolygons=lampSolids.AsReadOnly();
            IsEnabled=true;

            CityExteriorStairFlightDescriptor Flight(string id,Vector3 start)=>new(id,start,-Direction,
                CityTerracePilotPlan.FlightWidth,CityTerracePilotPlan.StepsPerFlight,
                rise/CityTerracePilotPlan.StepsPerFlight,CityTerracePilotPlan.TreadDepth,CityExteriorStairDropSide.Both);
        }

        public bool ContainsFootprint(Vector2 p)
        {
            foreach(var polygon in SurfacePolygons) if(CityRoadPolygon.Contains(polygon,p)) return true;
            return false;
        }
        public bool TrySampleTop(Vector2 p,out float top)
        {
            top=0f;if(!IsEnabled) return false;
            foreach(var approach in ApproachLandings)
                if(CityRoadPolygon.Contains(approach.Footprint,p))
                {
                    Vector2 from=Xz(approach.Start),delta=Xz(approach.End)-from;
                    float amount=Mathf.Clamp01(Vector2.Dot(p-from,delta)/delta.sqrMagnitude);
                    top=Mathf.Lerp(approach.Start.y,approach.End.y,amount);return true;
                }
            foreach(var landing in Landings)
                if(CityRoadPolygon.Contains(landing.Footprint,p)) {top=landing.Start.y;return true;}
            for(int i=0;i<Flights.Count;i++)
                if(CityRoadPolygon.Contains(flightFootprints[i],p))
                {
                    var flight=Flights[i];
                    float along=Vector3.Dot(new Vector3(p.x,flight.Start.y,p.y)-flight.Start,flight.Direction);
                    top=flight.Start.y+Mathf.Clamp01(along/flight.RunLength)*flight.TotalRise;return true;
                }
            if(footwaySeam!=null && CityRoadPolygon.Contains(footwaySeam,p)) {top=LowerDock.y;return true;}
            return false;
        }
        private void ValidateClearance(CityLayout layout,CityHillsideFurniturePlacement bench)
        {
            foreach(Rect reserved in CityBusNetworkFurniture.Reservations(CityDistrictBusPlanner.CreateRoadRouting(layout),layout))
                foreach(var polygon in SurfacePolygons)
                    if(reserved.Overlaps(CityRoadPolygon.Bounds(polygon)))
                        throw new InvalidOperationException("Pawnshop descent must retain every bus platform and queue.");
            foreach(var house in CityHillsideNeighborhoodPlan.ForLayout(layout).AllBuildings)
                foreach(var polygon in SurfacePolygons)
                    if(house.FoundationBounds.Overlaps(CityRoadPolygon.Bounds(polygon)))
                        throw new InvalidOperationException("Pawnshop descent overlaps a hillside house.");
            foreach(var detail in CityHillsideSiteDetails.ForLayout(layout))
            {
                if(detail.Part!=CityHillsideSiteDetails.Kind.YardTree) continue;
                // A mature crown may cover a path. Its actual trunk and a body
                // margin remain clear; no existing tree is moved for the stair.
                float radius=.34f*detail.Scale+.22f;
                foreach(var landing in Landings)
                    if(DistanceToSegment(Xz(detail.Origin),Xz(landing.Start),Xz(landing.End))<landing.Width*.5f+radius)
                        throw new InvalidOperationException("Pawnshop descent landing is too close to an existing tree.");
                foreach(var flight in Flights)
                    if(DistanceToSegment(Xz(detail.Origin),Xz(flight.Start),Xz(flight.End))<1.05f+radius)
                        throw new InvalidOperationException("Pawnshop descent stair is too close to an existing tree.");
                foreach(var approach in ApproachLandings)
                    if(DistanceToSegment(Xz(detail.Origin),Xz(approach.Start),Xz(approach.End))<approach.Width*.5f+radius)
                        throw new InvalidOperationException("Pawnshop descent approach is too close to an existing tree.");
            }
        }
        private static Vector2[] FlightPolygon(CityExteriorStairFlightDescriptor flight)
        {
            Vector3 side=flight.Right*1.05f;
            return CityRoadPolygon.CounterClockwise(new[] {Xz(flight.Start-side),Xz(flight.Start+side),Xz(flight.End+side),Xz(flight.End-side)});
        }
        private static float DistanceToSegment(Vector2 p,Vector2 a,Vector2 b)
        {Vector2 d=b-a;return Vector2.Distance(p,a+d*Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude));}
        private static Vector2 Xz(Vector3 p)=>new(p.x,p.z);
    }
}
