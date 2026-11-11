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

    /// <summary>A finite municipal stair from the summit footway, past the Home bench, to the turn below.</summary>
    public sealed class CityPawnshopDescentPlan
    {
        public const string BenchId=CityHillsideFurniturePlan.HomeBenchId;
        public const float LandingWidth=2.4f;
        internal const string SummitApronId = "Descent summit apron 0";
        private static readonly ConditionalWeakTable<CityLayout,CityPawnshopDescentPlan> Plans=new();
        public static CityPawnshopDescentPlan ForLayout(CityLayout layout)=>
            Plans.GetValue(layout,value=>new CityPawnshopDescentPlan(value));
        public bool IsEnabled {get;}
        public Vector3 BenchDock {get;}
        public Vector3 UpperDock {get;}
        public Vector3 BenchLandingDock {get;}
        public float SummitPavementNorthEdge {get;}
        internal IReadOnlyList<Vector2> SummitApronPaving { get; }
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
        public IReadOnlyList<Vector3> BenchRoute {get;}
        public IReadOnlyList<Vector3> ToPawnshopRouteWorld=>PawnshopRoute;
        public IReadOnlyList<Vector3> LampFeet {get;}
        public IReadOnlyList<Vector3> LampTargets {get;}
        private Vector2[] footwaySeam;
        private Vector2[][] flightFootprints=Array.Empty<Vector2[]>();

        private CityPawnshopDescentPlan(CityLayout layout)
        {
            Flights=Array.Empty<CityExteriorStairFlightDescriptor>();Landings=Array.Empty<CityPawnshopDescentLanding>();
            Footprints=Array.Empty<Vector2[]>();StairRoute=RouteWorld=PawnshopRoute=Array.Empty<Vector3>();
            SurfacePolygons=ApproachPolygons=Array.Empty<Vector2[]>();
            ApproachLandings=Array.Empty<CityPawnshopDescentLanding>();
            LampFeet=LampTargets=BenchRoute=Array.Empty<Vector3>();
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
            // Retain the seat branch and the existing lower stair. Its old
            // upper dock is below the summit: it is only an intermediate join.
            BenchLandingDock=bench.Position-right*2f-bench.Forward*1.7f;
            if(!ground.TrySampleTop(Xz(BenchLandingDock),out float upper))
                throw new InvalidOperationException("Pawnshop descent upper dock has no existing bank support.");
            BenchLandingDock=new Vector3(BenchLandingDock.x,upper,BenchLandingDock.z);
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
            Vector3 span=Vector3.ProjectOnPlane(LowerLandingEnd-BenchLandingDock,Vector3.up);
            Direction=span.normalized;
            float apron=(span.magnitude-2f*CityTerracePilotPlan.FlightRun-2.4f)*.5f;
            float rise=(upper-lower)*.5f;
            if(apron<1.2f || rise<=0f || rise/CityTerracePilotPlan.StepsPerFlight>.18f)
                throw new InvalidOperationException("Pawnshop descent needs comfortable aprons and ordinary stair rise.");
            Vector3 high=BenchLandingDock+Direction*apron;
            Vector3 first=high+Direction*CityTerracePilotPlan.FlightRun-Vector3.up*rise;
            Vector3 middle=first+Direction*2.4f;
            Vector3 bottom=middle+Direction*CityTerracePilotPlan.FlightRun-Vector3.up*rise;
            var flights=new List<CityExteriorStairFlightDescriptor> {
                Flight("Pawnshop descent upper flight",first),Flight("Pawnshop descent lower flight",bottom)};
            var landings=new List<CityPawnshopDescentLanding> {
                new CityPawnshopDescentLanding("Descent upper apron",BenchLandingDock,high,LandingWidth),
                new CityPawnshopDescentLanding("Descent view landing",first,middle,LandingWidth),
                new CityPawnshopDescentLanding("Descent lower apron",bottom,LowerLandingEnd,LandingWidth)};
            Vector3 pocketCorner=bench.Position-right*2.25f+bench.Forward*1.45f;
            if(!ground.TrySampleTop(Xz(pocketCorner),out float cornerTop))
                throw new InvalidOperationException("Pawnshop descent branch must join the real bench pocket.");
            pocketCorner=new Vector3(pocketCorner.x,cornerTop,pocketCorner.z);
            UpperApproachMouth=BenchLandingDock-Direction*.9f;
            ApproachLandings=Array.AsReadOnly(new[] {
                new CityPawnshopDescentLanding("Descent pocket branch",pocketCorner,UpperApproachMouth,1.6f),
                new CityPawnshopDescentLanding("Descent rear mouth",UpperApproachMouth,BenchLandingDock,1.6f)});

            // A measured diagonal fits three ordinary imported flights between
            // the actual summit pavement and the bench mouth. Short level
            // aprons separate every flight; the seat and its tree stay clear.
            var summit=CityHillsideQuarterPlan.ForLayout(layout).Links[1];
            SummitPavementNorthEdge=summit.Bounds.yMax;
            const int summitFlights=3;
            const float summitApron=1.2f,streetApron=5f;
            // End south of the tree, then turn north into the original mouth.
            // The long top apron clears the entire pavement before descending.
            Vector3 summitEnd=UpperApproachMouth-Vector3.forward*1.6f;
            float summitRun=summitFlights*CityTerracePilotPlan.FlightRun+summitFlights*summitApron+streetApron;
            float streetZ=summit.NorthSidewalk[0].z+.5f;
            float depth=summitEnd.z-streetZ;
            if(depth<=0f || depth>=summitRun)
                throw new InvalidOperationException("Pawnshop descent summit connection cannot fit its flights.");
            float streetX=summitEnd.x-Mathf.Sqrt(summitRun*summitRun-depth*depth);
            if(!summit.TrySampleTop(new Vector2(streetX,streetZ),out float summitTop,out var summitKind) ||
                summitKind!=CityStreetSupportKind.Sidewalk)
                throw new InvalidOperationException("Pawnshop descent must start on the summit public pavement.");
            UpperDock=new Vector3(streetX,summitTop,streetZ);
            Vector3 summitDirection=Vector3.ProjectOnPlane(summitEnd-UpperDock,Vector3.up).normalized;
            float summitRise=(summitTop-upper)/summitFlights;
            float summitStep=summitRise/CityTerracePilotPlan.StepsPerFlight;
            if(summitStep<.12f || summitStep>.18f)
                throw new InvalidOperationException("Pawnshop summit flights need ordinary stair rise.");
            var summitRoute=new List<Vector3> {UpperDock};
            Vector3 cursor=UpperDock;
            for(int i=0;i<summitFlights;i++)
            {
                Vector3 flightTop=cursor+summitDirection*(i==0?streetApron:summitApron);
                landings.Add(new CityPawnshopDescentLanding("Descent summit apron "+i,cursor,flightTop,LandingWidth));
                Vector3 flightBottom=flightTop+summitDirection*CityTerracePilotPlan.FlightRun-Vector3.up*summitRise;
                flights.Add(new CityExteriorStairFlightDescriptor("Pawnshop summit flight "+i,flightBottom,-summitDirection,
                    CityTerracePilotPlan.FlightWidth,CityTerracePilotPlan.StepsPerFlight,summitStep,
                    CityTerracePilotPlan.TreadDepth,CityExteriorStairDropSide.Both));
                summitRoute.Add(flightTop);summitRoute.Add(flightBottom);cursor=flightBottom;
            }
            landings.Add(new CityPawnshopDescentLanding("Descent summit bench apron",cursor,summitEnd,LandingWidth));
            // A full-width pad surrounds the turning point. Two ribbons that
            // only touch at their centres leave an earth wedge under the body.
            landings.Add(new CityPawnshopDescentLanding("Descent summit bench join",
                summitEnd-Vector3.forward*(LandingWidth*.5f),UpperApproachMouth,LandingWidth));
            summitRoute.Add(summitEnd);
            summitRoute.Add(UpperApproachMouth);
            Flights=flights.AsReadOnly();Landings=landings.AsReadOnly();
            var footprints=new List<Vector2[]>();
            foreach(var landing in Landings)
            {
                if (landing.Id != SummitApronId)
                {
                    footprints.Add(landing.Footprint);
                    continue;
                }
                // The first apron enters the north footway diagonally. Its actual
                // paving starts at the curb edge; the street owns the shared entry.
                Vector2 edge = new(0f, SummitPavementNorthEdge);
                Vector2[] clipped = CityRoadPolygon.Clip(landing.Footprint, edge, edge + Vector2.right).ToArray();
                if (CityRoadPolygon.Area(clipped) <= .00001f)
                    throw new InvalidOperationException("The summit apron has no paving outside the sidewalk.");
                SummitApronPaving = Array.AsReadOnly(clipped);
                footprints.Add(clipped);
            }
            flightFootprints=new Vector2[flights.Count][];
            for(int i=0;i<flights.Count;i++) flightFootprints[i]=FlightPolygon(flights[i]);
            footprints.AddRange(flightFootprints);
            Footprints=footprints.AsReadOnly();
            var approachPolygons=new List<Vector2[]>(2);
            var surfaces=new List<Vector2[]>(footprints);
            foreach(var approach in ApproachLandings) {approachPolygons.Add(approach.Footprint);surfaces.Add(approach.Footprint);}
            ApproachPolygons=approachPolygons.AsReadOnly();SurfacePolygons=surfaces.AsReadOnly();
            Vector2 minimum=new(float.PositiveInfinity,float.PositiveInfinity),maximum=-minimum;
            foreach(var polygon in SurfacePolygons) foreach(var p in polygon) {minimum=Vector2.Min(minimum,p);maximum=Vector2.Max(maximum,p);}
            Bounds=Rect.MinMaxRect(minimum.x,minimum.y,maximum.x,maximum.y);
            footwaySeam=new CityPawnshopDescentLanding("Descent footway seam",LowerLandingEnd,LowerDock,LandingWidth).Footprint;
            ValidateClearance(layout,bench);
            BenchRoute=Array.AsReadOnly(new[] {BenchDock,pocketCorner,UpperApproachMouth,BenchLandingDock});
            summitRoute.AddRange(new[] {BenchLandingDock,high,first,middle,bottom,LowerLandingEnd,LowerDock});
            StairRoute=summitRoute.AsReadOnly();
            var route=new List<Vector3>(StairRoute);
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
            Vector3 summitRight=Vector3.Cross(Vector3.up,summitDirection);
            LampFeet=Array.AsReadOnly(new[] {Landings[0].Center+railRight*1.07f,Landings[1].Center-railRight*1.07f,
                Landings[3].Center-summitRight*1.07f,Landings[5].Center-summitRight*1.07f});
            LampTargets=Array.AsReadOnly(new[] {first,bottom,(Flights[2].Start+Flights[2].End)*.5f,
                (Flights[4].Start+Flights[4].End)*.5f});
            var lampSolids=new List<Vector2[]>(LampFeet.Count);
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
                        throw new InvalidOperationException($"Pawnshop descent landing {landing.Id} is too close to tree {detail.Origin}.");
                foreach(var flight in Flights)
                    if(DistanceToSegment(Xz(detail.Origin),Xz(flight.Start),Xz(flight.End))<1.05f+radius)
                        throw new InvalidOperationException($"Pawnshop descent stair {flight.Id} is too close to tree {detail.Origin}.");
                foreach(var approach in ApproachLandings)
                    if(DistanceToSegment(Xz(detail.Origin),Xz(approach.Start),Xz(approach.End))<approach.Width*.5f+radius)
                        throw new InvalidOperationException("Pawnshop descent approach is too close to an existing tree.");
            }
        }
        private static Vector2[] FlightPolygon(CityExteriorStairFlightDescriptor flight)
        {
            Vector3 side=flight.Right*1.05f;
            // Overlap the level aprons deliberately. Independently subtracting
            // merely touching polygons can leave a thin strip of native earth
            // across a seam, tall enough for the capsule to step onto.
            Vector3 seam=flight.Direction*.025f;
            Vector3 start=flight.Start-seam,end=flight.End+seam;
            return CityRoadPolygon.CounterClockwise(new[] {Xz(start-side),Xz(start+side),Xz(end+side),Xz(end-side)});
        }
        private static float DistanceToSegment(Vector2 p,Vector2 a,Vector2 b)
        {Vector2 d=b-a;return Vector2.Distance(p,a+d*Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude));}
        private static Vector2 Xz(Vector3 p)=>new(p.x,p.z);
    }
}
